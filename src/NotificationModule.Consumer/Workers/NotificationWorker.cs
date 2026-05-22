using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotificationModule.Consumer.Services;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Persistence;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationModule.Consumer.Workers;

public class NotificationWorker : BackgroundService
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
    private const string ExchangeName = "appointment.notifications";

    private static readonly string[] QueueNames =
    {
        "notifications.swiftsend",
        "notifications.securepost",
        "notifications.legacylink",
        "notifications.asyncflow"
    };

    private readonly NotificationDispatcher _dispatcher;
    private readonly DeliveryTrackingService _deliveryTracking;
    private readonly RabbitMqRepublisher _republisher;
    private readonly IDbContextFactory<SecretsDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationWorker> _logger;

    private IConnection? _connection;
    private readonly List<IModel> _channels = new();

    public NotificationWorker(
        NotificationDispatcher dispatcher,
        DeliveryTrackingService deliveryTracking,
        RabbitMqRepublisher republisher,
        IDbContextFactory<SecretsDbContext> dbFactory,
        IConfiguration config,
        ILogger<NotificationWorker> logger)
    {
        _dispatcher = dispatcher;
        _deliveryTracking = deliveryTracking;
        _republisher = republisher;
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForRabbitMqAsync(stoppingToken);

        foreach (var queue in QueueNames)
        {
            var channel = _connection!.CreateModel();
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
            _channels.Add(channel);

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.Received += async (_, ea) =>
            {
                string? mappedProvider = null;
                try
                {
                    var parentContext = Propagator.Extract(
                        default,
                        ea.BasicProperties,
                        ExtractTraceContextFromBasicProperties);

                    Baggage.Current = parentContext.Baggage;
                    using var activity = NotificationTelemetry.ActivitySource.StartActivity(
                        "rabbitmq.consume.appointment_notification",
                        ActivityKind.Consumer,
                        parentContext.ActivityContext);

                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var message = JsonSerializer.Deserialize<AppointmentMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (message is null)
                    {
                        RecordMessageFailed(queue, null, "deserialize");
                        channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                        return;
                    }

                    mappedProvider = NotificationQueueMapping.TryGetProviderName(queue);
                    RecordMessageReceived(queue, mappedProvider);

                    activity?.SetTag("messaging.rabbitmq.queue", queue);
                    activity?.SetTag("appointment.uuid", message.AppointmentUuid);
                    activity?.SetTag("organization.key", message.OrganizationKey);
                    activity?.SetTag("scheduled_notification.id", message.ScheduledNotificationId?.ToString());

                    if (mappedProvider is null)
                        throw new InvalidOperationException($"Queue '{queue}' is not mapped to a provider.");

                    await DispatchAndTrackAsync(message, queue, mappedProvider, stoppingToken);
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message. Nacking.");
                    RecordMessageFailed(queue, mappedProvider, "exception");
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            channel.BasicConsume(queue, autoAck: false, consumer: consumer);
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static IEnumerable<string> ExtractTraceContextFromBasicProperties(IBasicProperties properties, string key)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(key, out var value))
            return Enumerable.Empty<string>();

        if (value is byte[] bytes)
            return [Encoding.UTF8.GetString(bytes)];

        if (value is string str)
            return [str];

        return Enumerable.Empty<string>();
    }

    private async Task DispatchAndTrackAsync(
        AppointmentMessage message,
        string queue,
        string providerName,
        CancellationToken stoppingToken)
    {
        var normalized = EnsureAttemptRecorded(message, providerName);
        var result = await _dispatcher.DispatchToProviderAsync(
            normalized,
            providerName,
            stoppingToken);

        if (!result.Success)
            RecordMessageFailed(queue, providerName, "dispatch");

        await _deliveryTracking.RecordAsync(
            normalized,
            providerName,
            result.Success,
            result.ErrorMessage,
            stoppingToken,
            result.ErrorType);

        if (!result.Success)
            await TryRepublishToFallbackAsync(normalized, providerName, stoppingToken);
    }

    private static AppointmentMessage EnsureAttemptRecorded(AppointmentMessage message, string providerName)
    {
        if (!string.IsNullOrWhiteSpace(message.TargetProvider))
            return message;

        return message with { TargetProvider = providerName };
    }

    private async Task TryRepublishToFallbackAsync(
        AppointmentMessage message,
        string failedProvider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.OrganizationKey))
            return;

        var tried = ParseProviders(message.TriedProviders);
        tried.Add(failedProvider);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var organization = await db.Organizations
            .SingleOrDefaultAsync(x => x.Key == message.OrganizationKey, ct);
        if (organization is null)
            return;

        var chain = BuildProviderChain(organization);
        var next = chain.FirstOrDefault(p => !tried.Contains(p));
        if (string.IsNullOrWhiteSpace(next))
            return;

        var updated = message with { TriedProviders = string.Join(",", tried) };
        _logger.LogWarning(
            "Provider {FailedProvider} failed for scheduled notification {ScheduledNotificationId}; falling back to {NextProvider}.",
            failedProvider,
            message.ScheduledNotificationId,
            next);

        _republisher.Republish(updated, next);
    }

    private static HashSet<string> ParseProviders(string? value)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return set;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(part);

        return set;
    }

    private static string[] BuildProviderChain(OrganizationRecord organization)
    {
        var providers = new List<string>();
        if (!string.IsNullOrWhiteSpace(organization.PreferredProvider))
            providers.Add(organization.PreferredProvider.Trim());

        if (!string.IsNullOrWhiteSpace(organization.FallbackProviders))
        {
            providers.AddRange(organization.FallbackProviders
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return providers
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void RecordMessageReceived(string queue, string? provider)
    {
        if (provider is not null)
        {
            NotificationTelemetry.NotificationMessagesReceived.Add(
                1,
                new KeyValuePair<string, object?>("queue", queue),
                new KeyValuePair<string, object?>("provider", provider));
            return;
        }

        NotificationTelemetry.NotificationMessagesReceived.Add(
            1,
            new KeyValuePair<string, object?>("queue", queue));
    }

    private static void RecordMessageFailed(string queue, string? provider, string failureReason)
    {
        if (provider is not null)
        {
            NotificationTelemetry.NotificationMessagesFailed.Add(
                1,
                new KeyValuePair<string, object?>("queue", queue),
                new KeyValuePair<string, object?>("provider", provider),
                new KeyValuePair<string, object?>("failure_reason", failureReason));
            return;
        }

        NotificationTelemetry.NotificationMessagesFailed.Add(
            1,
            new KeyValuePair<string, object?>("queue", queue),
            new KeyValuePair<string, object?>("failure_reason", failureReason));
    }

    private async Task WaitForRabbitMqAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMq:Host"] ?? "localhost",
            Port = int.Parse(_config["RabbitMq:Port"] ?? "5672"),
            UserName = _config["RabbitMq:Username"] ?? "guest",
            Password = _config["RabbitMq:Password"] ?? "guest",
            DispatchConsumersAsync = true,
        };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = factory.CreateConnection();
                using var topologyChannel = _connection.CreateModel();
                DeclareTopology(topologyChannel);
                _logger.LogInformation("Connected to RabbitMQ.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ not ready: {Msg}. Retrying in 3s…", ex.Message);
                await Task.Delay(3000, ct);
            }
        }
    }

    private static void DeclareTopology(IModel channel)
    {
        channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);

        foreach (var queue in QueueNames)
        {
            channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
            var providerName = NotificationQueueMapping.TryGetProviderName(queue)
                ?? throw new InvalidOperationException($"Queue '{queue}' is not mapped to a provider.");
            channel.QueueBind(queue, ExchangeName, routingKey: providerName);
        }
    }

    public override void Dispose()
    {
        foreach (var ch in _channels)
        {
            try { ch.Close(); } catch { }
            try { ch.Dispose(); } catch { }
        }
        _connection?.Close();
        base.Dispose();
    }
}
