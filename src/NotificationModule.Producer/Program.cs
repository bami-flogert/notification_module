using Microsoft.EntityFrameworkCore;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["NotificationDb:ConnectionString"]
    ?? throw new InvalidOperationException("NotificationDb:ConnectionString is required.");

builder.Services.AddControllers();
builder.Services.AddDbContextFactory<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddScoped<AppointmentIngestionService>();
builder.Services.AddHostedService<NotificationSchedulerWorker>();

var app = builder.Build();

app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<NotificationDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

var publisher = app.Services.GetRequiredService<RabbitMqPublisher>();
publisher.Initialize();

await app.RunAsync();
