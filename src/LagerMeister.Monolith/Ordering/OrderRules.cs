namespace LagerMeister.Monolith.Ordering;

public record OrderLineRequest(string Sku, int Quantity);

public record OrderRequest(string CustomerRef, IReadOnlyList<OrderLineRequest> Lines);

public enum OrderRejection
{
    None,
    EmptyOrder,
    NonPositiveQuantity,
    UnknownSku,
    InsufficientStock
}

// Pure order-admission rules — no DbContext, no I/O, no clock.
// Everything here is deterministic and unit-testable in isolation; the DB-bound
// orchestration lives in OrderService. Keeping the decision here is what lets the
// `order.validate` span wrap real business logic rather than a database round-trip.
public static class OrderRules
{
    /// <summary>
    /// Decide whether an order may be admitted, given the current on-hand stock keyed by SKU.
    /// A SKU may legitimately appear on multiple lines; quantities are aggregated per SKU
    /// before the stock check so split lines cannot bypass the limit.
    /// </summary>
    public static OrderRejection Validate(OrderRequest request, IReadOnlyDictionary<string, int> stockBySku)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            return OrderRejection.EmptyOrder;

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0)
                return OrderRejection.NonPositiveQuantity;
            if (!stockBySku.ContainsKey(line.Sku))
                return OrderRejection.UnknownSku;
        }

        foreach (var group in request.Lines.GroupBy(l => l.Sku))
        {
            var required = group.Sum(l => l.Quantity);
            if (required > stockBySku[group.Key])
                return OrderRejection.InsufficientStock;
        }

        return OrderRejection.None;
    }

    /// <summary>Net quantity required per SKU after aggregating split lines.</summary>
    public static IReadOnlyDictionary<string, int> RequiredBySku(OrderRequest request) =>
        request.Lines
            .GroupBy(l => l.Sku)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
}
