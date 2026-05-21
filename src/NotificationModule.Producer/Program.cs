using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotificationModule.Shared.Observability;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["NotificationDb:ConnectionString"]
    ?? throw new InvalidOperationException("NotificationDb:ConnectionString is required.");
var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];

builder.Services.AddControllers();
builder.Services.AddDbContextFactory<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddScoped<AppointmentIngestionService>();
builder.Services.AddHostedService<NotificationSchedulerWorker>();
builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, name: "notification-db", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService(
            serviceName: builder.Configuration["OpenTelemetry:ServiceName"] ?? "notification-producer",
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
            .AddAspNetCoreInstrumentation()
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
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    options.Endpoint = new Uri(otlpEndpoint);
            });
    });

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<NotificationDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

var publisher = app.Services.GetRequiredService<RabbitMqPublisher>();
publisher.Initialize();

await app.RunAsync();
