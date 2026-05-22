using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotificationModule.Consumer.Adapters;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Consumer.Services;
using NotificationModule.Consumer.Workers;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["SecretsDb:ConnectionString"]
    ?? throw new InvalidOperationException("SecretsDb:ConnectionString is required.");

builder.AddNotificationOpenTelemetry("notification-consumer");

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

builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, name: "secrets-db", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<SecretsInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

await app.RunAsync();
