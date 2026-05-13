namespace Czanix.Domain.Entities;

public class Order
{
    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public List<OrderItem> Items { get; private set; } = [];
    public string Status { get; private set; } = "pending";
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    public decimal Total => Items.Sum(i => i.Quantity * i.UnitPrice);

    private Order() { }

    public static Order Create(string customerId, List<OrderItem> items)
    {
        if (items is null || items.Count == 0)
            throw new ArgumentException("Pedido deve ter pelo menos um item");

        return new Order
        {
            PublicId = Guid.NewGuid(),
            CustomerId = customerId,
            Items = items,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Cancel()
    {
        if (Status == "delivered") throw new InvalidOperationException("Cannot cancel delivered order");
        if (Status == "cancelled") throw new InvalidOperationException("Already cancelled");
        Status = "cancelled";
        UpdatedAt = DateTime.UtcNow;
    }
}

public record OrderItem(string ProductId, int Quantity, decimal UnitPrice)
{
    public OrderItem
    {
        if (Quantity <= 0) throw new ArgumentException("Quantity must be positive");
        if (UnitPrice < 0) throw new ArgumentException("Price cannot be negative");
    }
}
