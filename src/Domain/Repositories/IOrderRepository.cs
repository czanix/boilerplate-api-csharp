namespace Czanix.Domain.Repositories;

using Czanix.Domain.Entities;

public interface IOrderRepository
{
    Task SaveAsync(Order order, CancellationToken ct = default);
    Task<Order?> FindByPublicIdAsync(Guid publicId, CancellationToken ct = default);
    Task UpdateAsync(Order order, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid publicId, CancellationToken ct = default);
}
