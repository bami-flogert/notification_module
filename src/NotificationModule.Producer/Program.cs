using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotificationModule.Producer.OpenMrs;
using NotificationModule.Producer.Security;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

var isTesting = builder.Environment.IsEnvironment("Testing");
var connectionString = builder.Configuration["NotificationDb:ConnectionString"]
    ?? throw new InvalidOperationException("NotificationDb:ConnectionString is required.");

builder.AddNotificationOpenTelemetry("notification-producer");

builder.Services.AddControllers();
builder.Services.AddDbContextFactory<NotificationDbContext>(options =>
{
    if (isTesting)
    {
        var testingDatabaseName = builder.Configuration["NotificationDb:TestingDatabaseName"] ?? "IntegrationTests";
        options.UseInMemoryDatabase(testingDatabaseName);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<INotificationMessagePublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddScoped<AppointmentIngestionService>();
builder.Services.AddSingleton<OpenMrsWebhookMapper>();
builder.Services.AddSingleton<OrganizationApiKeyService>();
builder.Services.AddSingleton<OrganizationProviderPolicyService>();
builder.Services.AddSingleton<BillingDeliveriesReportService>();
builder.Services.AddScoped<AppointmentApiKeyAuthFilter>();

if (!isTesting)
{
    builder.Services.AddHostedService<NotificationSchedulerWorker>();
    builder.Services.AddHostedService<DataRetentionWorker>();
}

var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"]);

if (!isTesting)
{
    healthChecks
        .AddNpgSql(connectionString, name: "notification-db", tags: ["ready"])
        .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
}

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
    if (isTesting)
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();

    var apiKeys = scope.ServiceProvider.GetRequiredService<OrganizationApiKeyService>();
    var defaultOrgKey = app.Configuration["Organizations:Default:Key"] ?? "default";
    var seedKey = app.Configuration["ApiKeys:Seed:Default"];
    if (!string.IsNullOrWhiteSpace(seedKey))
        await apiKeys.EnsureSeededAsync(defaultOrgKey, seedKey, app.Lifetime.ApplicationStopping);
}

if (!isTesting)
{
    var publisher = app.Services.GetRequiredService<RabbitMqPublisher>();
    publisher.Initialize();
}

await app.RunAsync();

public partial class Program;
