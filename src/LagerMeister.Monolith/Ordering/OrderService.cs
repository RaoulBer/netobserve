using System.Diagnostics;
using LagerMeister.Monolith.Data;
using LagerMeister.Monolith.Models;
using LagerMeister.Monolith.Observability;
using Microsoft.EntityFrameworkCore;

namespace LagerMeister.Monolith.Ordering;

public record OrderResult(bool Ok, int OrderId, OrderRejection Rejection)
{
    public static OrderResult Success(int id) => new(true, id, OrderRejection.None);
    public static OrderResult Rejected(OrderRejection reason) => new(false, 0, reason);
}

// Legacy-style orchestration: direct DbContext usage, three explicit steps
// (validate -> reserve stock -> persist), with manual `order.validate` /
// `stock.reserve` spans wrapping the decision and the stock mutation.
public class OrderService
{
    private const int ReservationLocationId = 1; // "RECEIVING" dock — where reservations debit from
    private readonly WarehouseDbContext _db;
    private readonly ILogger<OrderService> _log;

    public OrderService(WarehouseDbContext db, ILogger<OrderService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var skus = request.Lines.Select(l => l.Sku).Distinct().ToList();
        var items = await _db.Items.Where(i => skus.Contains(i.Sku)).ToListAsync(ct);
        var stockBySku = items.ToDictionary(i => i.Sku, i => i.QuantityOnHand);

        // Step 1: validate (pure decision) — wrapped in a manual span so the admission
        // decision, not just the DB round-trip, is visible in the trace.
        OrderRejection rejection;
        using (var validate = Diagnostics.ActivitySource.StartActivity("order.validate"))
        {
            validate?.SetTag(AppAttributes.OrderCustomerRef, request.CustomerRef);
            validate?.SetTag(AppAttributes.OrderLineCount, request.Lines.Count);
            rejection = OrderRules.Validate(request, stockBySku);
            if (rejection != OrderRejection.None)
            {
                validate?.SetTag(AppAttributes.OrderRejection, rejection.ToString());
                // Mark the business rejection as an error on the span so tail-sampling
                // error policies and the Datadog error-rate monitor have a signal to key on.
                validate?.SetStatus(ActivityStatusCode.Error, rejection.ToString());
            }
        }

        if (rejection != OrderRejection.None)
        {
            _log.LogWarning("Order for {CustomerRef} rejected: {Rejection}", request.CustomerRef, rejection);
            return OrderResult.Rejected(rejection);
        }

        // Step 2: reserve stock (mutate on-hand + record movements)
        var required = OrderRules.RequiredBySku(request);
        var itemsBySku = items.ToDictionary(i => i.Sku);
        using (var reserve = Diagnostics.ActivitySource.StartActivity("stock.reserve"))
        {
            reserve?.SetTag(AppAttributes.ReservedSkuCount, required.Count);
            foreach (var (sku, qty) in required)
            {
                var item = itemsBySku[sku];
                item.QuantityOnHand -= qty;
                _db.StockMovements.Add(new StockMovement
                {
                    ItemId = item.Id,
                    LocationId = ReservationLocationId,
                    Delta = -qty,
                    OccurredAt = DateTime.UtcNow
                });
            }
        }

        // Step 3: persist the order
        var order = new Order
        {
            CustomerRef = request.CustomerRef,
            Status = "RESERVED",
            CreatedAt = DateTime.UtcNow,
            Lines = request.Lines
                .Select(l => new OrderLine { ItemId = itemsBySku[l.Sku].Id, Quantity = l.Quantity })
                .ToList()
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        Activity.Current?.SetTag(AppAttributes.OrderId, order.Id);
        _log.LogInformation("Order {OrderId} reserved for {CustomerRef} ({LineCount} lines)",
            order.Id, request.CustomerRef, order.Lines.Count);
        return OrderResult.Success(order.Id);
    }
}
