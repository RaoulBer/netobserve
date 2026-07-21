using LagerMeister.Monolith.Ordering;
using Xunit;

namespace LagerMeister.Monolith.Tests;

// The order-admission decision is pure and DB-free, so it is unit-tested in isolation.
// These tests are the contract the `order.validate` span wraps at runtime.
public class OrderRulesTests
{
    private static readonly IReadOnlyDictionary<string, int> Stock = new Dictionary<string, int>
    {
        ["WIDGET-001"] = 120,
        ["GEAR-014"] = 40,
        ["CRATE-XL"] = 8,
    };

    private static OrderRequest Order(params OrderLineRequest[] lines) =>
        new("ACME", lines);

    [Fact]
    public void Empty_order_is_rejected()
    {
        Assert.Equal(OrderRejection.EmptyOrder, OrderRules.Validate(Order(), Stock));
    }

    [Fact]
    public void Null_lines_are_rejected_as_empty()
    {
        var request = new OrderRequest("ACME", null!);
        Assert.Equal(OrderRejection.EmptyOrder, OrderRules.Validate(request, Stock));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_quantity_is_rejected(int qty)
    {
        var request = Order(new OrderLineRequest("WIDGET-001", qty));
        Assert.Equal(OrderRejection.NonPositiveQuantity, OrderRules.Validate(request, Stock));
    }

    [Fact]
    public void Unknown_sku_is_rejected()
    {
        var request = Order(new OrderLineRequest("DOES-NOT-EXIST", 1));
        Assert.Equal(OrderRejection.UnknownSku, OrderRules.Validate(request, Stock));
    }

    [Fact]
    public void Order_within_stock_is_admitted()
    {
        var request = Order(
            new OrderLineRequest("WIDGET-001", 3),
            new OrderLineRequest("GEAR-014", 2));
        Assert.Equal(OrderRejection.None, OrderRules.Validate(request, Stock));
    }

    [Fact]
    public void Quantity_exactly_equal_to_stock_is_admitted()
    {
        var request = Order(new OrderLineRequest("CRATE-XL", 8));
        Assert.Equal(OrderRejection.None, OrderRules.Validate(request, Stock));
    }

    [Fact]
    public void Quantity_one_over_stock_is_rejected()
    {
        var request = Order(new OrderLineRequest("CRATE-XL", 9));
        Assert.Equal(OrderRejection.InsufficientStock, OrderRules.Validate(request, Stock));
    }

    // The interesting case: neither split line alone exceeds stock, but together they do.
    // Aggregation-per-SKU is what stops a client bypassing the limit by splitting lines.
    [Fact]
    public void Split_lines_for_same_sku_are_aggregated_before_the_stock_check()
    {
        var request = Order(
            new OrderLineRequest("CRATE-XL", 5),
            new OrderLineRequest("CRATE-XL", 5));
        Assert.Equal(OrderRejection.InsufficientStock, OrderRules.Validate(request, Stock));
    }

    [Fact]
    public void RequiredBySku_sums_split_lines()
    {
        var request = Order(
            new OrderLineRequest("CRATE-XL", 5),
            new OrderLineRequest("CRATE-XL", 2),
            new OrderLineRequest("WIDGET-001", 1));
        var required = OrderRules.RequiredBySku(request);
        Assert.Equal(7, required["CRATE-XL"]);
        Assert.Equal(1, required["WIDGET-001"]);
    }
}
