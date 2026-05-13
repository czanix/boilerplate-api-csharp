namespace Czanix.Application.UseCases;

using Czanix.Application.DTOs;
using Czanix.Domain.Common;
using Czanix.Domain.Entities;
using Czanix.Domain.Repositories;

public class CreateOrderUseCase(IOrderRepository repository)
{
    public async Task<Result<OrderOutput>> ExecuteAsync(CreateOrderInput input, CancellationToken ct = default)
    {
        if (input.Items is null || input.Items.Count == 0)
            return Result<OrderOutput>.Fail("Pedido deve ter pelo menos um item");

        var items = input.Items
            .Select(i => new OrderItem(i.ProductId, i.Quantity, i.UnitPrice))
            .ToList();

        var order = Order.Create(input.CustomerId, items);
        await repository.SaveAsync(order, ct);

        return Result<OrderOutput>.Ok(OrderOutput.From(order));
    }
}
