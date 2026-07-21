namespace LagerMeister.Inventory.Domain;

// Entity within the InventoryItem aggregate: one stock change at a named location.
// Positive Delta = inbound, negative = reservation/outbound.
public sealed record StockMovement(int Delta, DateTime OccurredAt, string LocationCode);
