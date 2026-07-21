using LagerMeister.Monolith.Data;
using LagerMeister.Monolith.Observability;
using LagerMeister.Monolith.Ordering;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;

// NLog remains the file/console logging pipeline; OpenTelemetry is layered on top
// (traces + metrics + a logs dual-write bridge). See docs/NLOG-BRIDGE.md.
var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    builder.AddLagerMeisterObservability();

    var connectionString = builder.Configuration.GetConnectionString("Warehouse")
        ?? "Host=localhost;Port=5432;Database=lagermeister;Username=postgres;Password=postgres";
    builder.Services.AddDbContext<WarehouseDbContext>(o => o.UseNpgsql(connectionString));
    builder.Services.AddScoped<OrderService>();
    builder.Services.AddControllers();

    // CORS for the browser frontend: it calls the API cross-origin and must be
    // allowed to send the W3C `traceparent`/`tracestate` headers so the backend
    // span continues the browser's trace.
    var frontendOrigins = (builder.Configuration["FRONTEND_ORIGINS"]
        ?? "http://localhost:8081,http://127.0.0.1:8081").Split(',', StringSplitOptions.RemoveEmptyEntries);
    builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
        .WithOrigins(frontendOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

    var app = builder.Build();

    // Apply schema + seed on startup (EnsureCreated, no migrations).
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        await SeedData.InitializeAsync(db);
    }

    app.UseCors("frontend");
    app.MapControllers();
    app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "lagermeister-monolith" }));

    logger.Info("LagerMeister monolith starting (OpenTelemetry + NLog bridge)");
    app.Run();
}
catch (Exception ex)
{
    logger.Error(ex, "LagerMeister monolith terminated unexpectedly");
    throw;
}
finally
{
    LogManager.Shutdown();
}
