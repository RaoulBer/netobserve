namespace LagerMeister.Inventory.Domain;

// Value object: a non-negative monetary amount with a currency. Rounds to 2 dp on
// construction so equality is well-behaved (4.50 == 4.500).
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Euro(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentException("Money cannot be negative.", nameof(amount));
        return new Money(decimal.Round(amount, 2, MidpointRounding.AwayFromZero), "EUR");
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
