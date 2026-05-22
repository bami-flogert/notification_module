using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NotificationModule.Shared.Observability;

public static class OpenTelemetryRegistration
{
    public static IHostApplicationBuilder AddNotificationOpenTelemetry(
        this IHostApplicationBuilder builder,
        string defaultServiceName)
    {
        var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];
        var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? defaultServiceName;
        var environment = builder.Configuration["OpenTelemetry:Environment"] ?? builder.Environment.EnvironmentName;
        var serviceVersion = typeof(OpenTelemetryRegistration).Assembly.GetName().Version?.ToString() ?? "unknown";

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(serviceName, serviceVersion: serviceVersion)
                    .AddAttributes(
                    [
                        new KeyValuePair<string, object>("deployment.environment", environment),
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
            })
            .WithLogging(logging =>
            {
                logging.AddOtlpExporter(options =>
                {
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                        options.Endpoint = new Uri(otlpEndpoint);
                });
            });

        return builder;
    }
}
