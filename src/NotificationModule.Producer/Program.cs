using NotificationModule.Producer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<RabbitMqPublisher>();

var app = builder.Build();

app.MapControllers();

var publisher = app.Services.GetRequiredService<RabbitMqPublisher>();
publisher.Initialize();

app.Run();
