using LagerMeister.Inventory.Domain;
using Xunit;

namespace LagerMeister.Inventory.Tests;

public class SkuTests
{
    [Fact]
    public void Create_normalizes_case_and_whitespace()
    {
        Assert.Equal("WIDGET-001", Sku.Create("  widget-001 ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_rejects_empty(string? raw)
    {
        Assert.Throws<ArgumentException>(() => Sku.Create(raw!));
    }

    [Fact]
    public void Create_rejects_overlong()
    {
        Assert.Throws<ArgumentException>(() => Sku.Create(new string('X', 33)));
    }

    [Fact]
    public void Two_skus_with_same_value_are_equal()
    {
        Assert.Equal(Sku.Create("bolt-m8"), Sku.Create("BOLT-M8"));
    }
}

public class MoneyTests
{
    [Fact]
    public void Euro_rounds_to_two_decimals()
    {
        Assert.Equal(4.51m, Money.Euro(4.505m).Amount);
    }

    [Fact]
    public void Euro_rejects_negative()
    {
        Assert.Throws<ArgumentException>(() => Money.Euro(-0.01m));
    }

    [Fact]
    public void Equality_ignores_trailing_zeros()
    {
        Assert.Equal(Money.Euro(4.50m), Money.Euro(4.5m));
    }
}

public class InventoryItemTests
{
    private static InventoryItem Item(int stock, params StockMovement[] movements) =>
        new(1, Sku.Create("WIDGET-001"), "Standard Widget", stock, Money.Euro(4.50m), movements);

    [Fact]
    public void Rejects_negative_stock()
    {
        Assert.Throws<ArgumentException>(() => Item(-1));
    }

    [Fact]
    public void Movements_are_exposed_newest_first()
    {
        var older = new StockMovement(5, new DateTime(2026, 1, 1), "AISLE-A");
        var newer = new StockMovement(7, new DateTime(2026, 3, 1), "AISLE-B");
        var item = Item(50, older, newer);
        Assert.Equal(newer, item.Movements[0]);
        Assert.Equal(older, item.Movements[1]);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(0, true)]
    public void IsLowStock_uses_threshold(int stock, bool expected)
    {
        Assert.Equal(expected, Item(stock).IsLowStock());
    }

    [Fact]
    public void RecentInboundUnits_sums_only_positive_deltas()
    {
        var item = Item(50,
            new StockMovement(5, new DateTime(2026, 1, 1), "A"),
            new StockMovement(-3, new DateTime(2026, 1, 2), "A"),
            new StockMovement(7, new DateTime(2026, 1, 3), "B"));
        Assert.Equal(12, item.RecentInboundUnits());
    }
}
