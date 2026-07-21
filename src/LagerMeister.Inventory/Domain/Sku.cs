namespace LagerMeister.Inventory.Domain;

// Value object: a Stock Keeping Unit. Self-validating, normalized, value-equal.
// Construction is the only way an invalid SKU could enter the domain, so it is
// guarded here rather than checked at every use site.
public sealed record Sku
{
    public string Value { get; }

    private Sku(string value) => Value = value;

    public static Sku Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("SKU must not be empty.", nameof(raw));

        var normalized = raw.Trim().ToUpperInvariant();
        if (normalized.Length > 32)
            throw new ArgumentException("SKU must be at most 32 characters.", nameof(raw));

        return new Sku(normalized);
    }

    public override string ToString() => Value;
}
