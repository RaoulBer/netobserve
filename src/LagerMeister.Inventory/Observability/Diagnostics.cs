using System.Diagnostics;

namespace LagerMeister.Inventory.Observability;

public static class Diagnostics
{
    public const string ServiceName = "lagermeister-inventory";
    public const string ActivitySourceName = "LagerMeister.Inventory";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
}

public static class AppAttributes
{
    public const string ItemId = "app.item.id";
    public const string ItemSku = "app.item.sku";
    public const string ItemLowStock = "app.item.low_stock";
    public const string MovementCount = "app.item.movement_count";
}
