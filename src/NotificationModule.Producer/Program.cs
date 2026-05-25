using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotificationModule.Producer.Fhir;
using NotificationModule.Producer.Security;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["NotificationDb:ConnectionString"]
    ?? throw new InvalidOperationException("NotificationDb:ConnectionString is required.");

builder.AddNotificationOpenTelemetry("notification-producer");

builder.Services.AddControllers();
builder.Services.AddDbContextFactory<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<INotificationMessagePublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddScoped<AppointmentIngestionService>();
builder.Services.AddSingleton<FhirAppointmentMapper>();
builder.Services.AddSingleton<FhirAppointmentValidator>();
builder.Services.AddSingleton<OrganizationApiKeyService>();
builder.Services.AddSingleton<OrganizationProviderPolicyService>();
builder.Services.AddSingleton<BillingDeliveriesReportService>();
builder.Services.AddScoped<AppointmentApiKeyAuthFilter>();
builder.Services.AddHostedService<NotificationSchedulerWorker>();
builder.Services.AddHostedService<DataRetentionWorker>();
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

    var apiKeys = scope.ServiceProvider.GetRequiredService<OrganizationApiKeyService>();
    var defaultOrgKey = app.Configuration["Organizations:Default:Key"] ?? "default";
    var seedKey = app.Configuration["ApiKeys:Seed:Default"];
    if (!string.IsNullOrWhiteSpace(seedKey))
        await apiKeys.EnsureSeededAsync(defaultOrgKey, seedKey, app.Lifetime.ApplicationStopping);
}

var publisher = app.Services.GetRequiredService<RabbitMqPublisher>();
publisher.Initialize();

await app.RunAsync();
