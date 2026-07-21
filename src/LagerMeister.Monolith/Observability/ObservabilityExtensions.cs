using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LagerMeister.Monolith.Observability;

// One instrumentation, one OTLP endpoint, N backends.
// The application knows only OTLP — which backend the telemetry lands in
// (Grafana LGTM, Datadog, Azure Monitor) is a Collector concern, not app code.
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddLagerMeisterObservability(this WebApplicationBuilder builder)
    {
        // OTLP export is enabled only when an endpoint is configured (standard
        // OTEL_EXPORTER_OTLP_ENDPOINT env var). Console export is a dev aid so spans
        // are visible without a running Collector (OTEL_DEV_CONSOLE=1).
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var otlpEnabled = !string.IsNullOrWhiteSpace(otlpEndpoint);
        var devConsole = Environment.GetEnvironmentVariable("OTEL_DEV_CONSOLE") == "1";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName: Diagnostics.ServiceName,
                serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Diagnostics.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql(); // subscribes to Npgsql's ActivitySource -> DB spans (the N+1)
                if (otlpEnabled) tracing.AddOtlpExporter();
                if (devConsole) tracing.AddConsoleExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (otlpEnabled) metrics.AddOtlpExporter();
                if (devConsole) metrics.AddConsoleExporter();
            });

        // Logs bridge (dual-write): the OpenTelemetry logging provider runs ALONGSIDE
        // NLog. Application code logs once via ILogger<T>; NLog writes file/console
        // (with TraceId in the layout) and OTel exports OTLP -> Loki. The OTel log
        // records inherit TraceId/SpanId from Activity.Current automatically.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(Diagnostics.ServiceName, serviceVersion: "1.0.0"));
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            if (otlpEnabled) logging.AddOtlpExporter();
            if (devConsole) logging.AddConsoleExporter();
        });

        return builder;
    }
}
