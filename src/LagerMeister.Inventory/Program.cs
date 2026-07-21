using LagerMeister.Inventory.Application;
using LagerMeister.Inventory.Domain;
using LagerMeister.Inventory.Infrastructure;
using LagerMeister.Inventory.Observability;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddInventoryObservability();

var connectionString = builder.Configuration.GetConnectionString("Warehouse")
    ?? "Host=localhost;Port=5432;Database=lagermeister;Username=postgres;Password=postgres";
builder.Services.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddScoped<IInventoryRepository, EfInventoryRepository>();
builder.Services.AddScoped<GetItemDetail>();

var app = builder.Build();

// The one endpoint strangled out of the monolith.
app.MapGet("/api/items/{id:int}", async (int id, GetItemDetail query, CancellationToken ct) =>
{
    var dto = await query.HandleAsync(id, ct);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "lagermeister-inventory" }));

app.Run();
