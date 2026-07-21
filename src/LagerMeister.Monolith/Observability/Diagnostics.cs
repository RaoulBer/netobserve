using System.Diagnostics;

namespace LagerMeister.Monolith.Observability;

// The one custom ActivitySource for the monolith. Manual spans (order.validate,
// stock.reserve) are created from here and joined to the ambient request trace.
public static class Diagnostics
{
    public const string ServiceName = "lagermeister-monolith";
    public const string ActivitySourceName = "LagerMeister.Monolith";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
}

// Domain attribute keys live under the app.* namespace (OTel leaves app-specific
// keys to the application; http.*/db.* are reserved for semantic conventions).
//
// CARDINALITY RULE: these high-cardinality values (customer refs, order ids) are
// allowed on SPANS but must never become METRIC labels. See docs/SAMPLING.md.
public static class AppAttributes
{
    public const string OrderCustomerRef = "app.order.customer_ref";
    public const string OrderLineCount = "app.order.line_count";
    public const string OrderRejection = "app.order.rejection";
    public const string OrderId = "app.order.id";
    public const string ReservedSkuCount = "app.stock.reserved_sku_count";
}
