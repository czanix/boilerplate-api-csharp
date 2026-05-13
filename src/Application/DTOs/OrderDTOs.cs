namespace Czanix.Application.DTOs;

using Czanix.Domain.Entities;

public record CreateOrderInput(string CustomerId, List<ItemInput> Items);
public record ItemInput(string ProductId, int Quantity, decimal UnitPrice);

public record OrderOutput(string PublicId, string CustomerId, string Status, decimal Total, string CreatedAt)
{
    public static OrderOutput From(Order order) => new(
        order.PublicId.ToString(), order.CustomerId, order.Status,
        order.Total, order.CreatedAt.ToString("O"));
}
