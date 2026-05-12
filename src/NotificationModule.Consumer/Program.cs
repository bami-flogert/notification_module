using NotificationModule.Consumer.Adapters;
using NotificationModule.Consumer.Services;
using NotificationModule.Consumer.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Register all provider adapters — add new ones here only
builder.Services.AddSingleton<INotificationProvider, SwiftSendProvider>();
builder.Services.AddSingleton<INotificationProvider, SecurePostProvider>();
builder.Services.AddSingleton<INotificationProvider, LegacyLinkProvider>();
builder.Services.AddSingleton<INotificationProvider, AsyncFlowProvider>();

builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddHostedService<NotificationWorker>();

var host = builder.Build();
host.Run();
