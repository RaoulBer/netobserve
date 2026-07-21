using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string ServiceName = "lagermeister-facade";

var builder = WebApplication.CreateBuilder(args);

var otlpEnabled = !string.IsNullOrWhiteSpace(
    Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ServiceName, serviceVersion: "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing
            // YARP's own ActivitySource: creates a forward span AND injects the trace
            // context into the downstream request, so InventoryService/monolith nest
            // UNDER the facade in the trace (the migration is literally visible).
            .AddSource("Yarp.ReverseProxy")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (otlpEnabled) tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (otlpEnabled) metrics.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName, serviceVersion: "1.0.0"));
    logging.IncludeFormattedMessage = true;
    if (otlpEnabled) logging.AddOtlpExporter();
});

// The facade is the public entry point, so the browser CORS policy lives here now.
var frontendOrigins = (builder.Configuration["FRONTEND_ORIGINS"]
    ?? "http://localhost:8081,http://127.0.0.1:8081").Split(',', StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(frontendOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = ServiceName }));
app.MapReverseProxy();

app.Run();
