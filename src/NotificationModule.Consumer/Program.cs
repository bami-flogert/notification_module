using Microsoft.EntityFrameworkCore;
using NotificationModule.Consumer.Adapters;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Consumer.Services;
using NotificationModule.Consumer.Workers;
using NotificationModule.Shared.Persistence;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration["SecretsDb:ConnectionString"]
    ?? throw new InvalidOperationException("SecretsDb:ConnectionString is required.");

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

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<SecretsInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

await host.RunAsync();
