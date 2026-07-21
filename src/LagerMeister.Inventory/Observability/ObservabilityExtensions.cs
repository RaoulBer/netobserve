using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LagerMeister.Inventory.Observability;

// OTel-native from the start (no NLog bridge — that is the legacy monolith's concern).
// Same OTLP-only posture: the app knows one endpoint; the Collector fans out.
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddInventoryObservability(this WebApplicationBuilder builder)
    {
        var otlpEnabled = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(Diagnostics.ServiceName, serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Diagnostics.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();
                if (otlpEnabled) tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (otlpEnabled) metrics.AddOtlpExporter();
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(Diagnostics.ServiceName, serviceVersion: "1.0.0"));
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            if (otlpEnabled) logging.AddOtlpExporter();
        });

        return builder;
    }
}
