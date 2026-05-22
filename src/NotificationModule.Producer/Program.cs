using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotificationModule.Shared.Observability;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["NotificationDb:ConnectionString"]
    ?? throw new InvalidOperationException("NotificationDb:ConnectionString is required.");

builder.AddNotificationOpenTelemetry("notification-producer");

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
