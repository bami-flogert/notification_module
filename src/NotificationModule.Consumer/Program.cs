using Microsoft.EntityFrameworkCore;
using NotificationModule.Consumer.Adapters;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Consumer.Services;
using NotificationModule.Consumer.Workers;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration["SecretsDb:ConnectionString"]
    ?? throw new InvalidOperationException("SecretsDb:ConnectionString is required.");
var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];

builder.Services.AddDbContextFactory<SecretsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsAssembly(typeof(NotificationDbContext).Assembly.FullName)));

builder.Services.AddSingleton<AesGcmSecretProtector>();
builder.Services.AddSingleton<ProviderSecretsStore>();
builder.Services.AddScoped<SecretsInitializer>();

builder.Services.AddSingleton<INotificationProvider, SwiftSendProvider>();
builder.Services.AddSingleton<INotificationProvider, SecurePostProvider>();
builder.Services.AddSingleton<INotificationProvider, LegacyLinkProvider>();
builder.Services.AddSingleton<INotificationProvider, AsyncFlowProvider>();

builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddSingleton<DeliveryTrackingService>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService(
            serviceName: builder.Configuration["OpenTelemetry:ServiceName"] ?? "notification-consumer",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown")
        .AddAttributes(
        [
            new KeyValuePair<string, object>("deployment.environment",
                builder.Configuration["OpenTelemetry:Environment"] ?? builder.Environment.EnvironmentName),
        ]);
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(NotificationTelemetry.ActivitySourceName)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    options.Endpoint = new Uri(otlpEndpoint);
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(NotificationTelemetry.MeterName)
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    options.Endpoint = new Uri(otlpEndpoint);
            });
    });

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<SecretsInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

await host.RunAsync();
