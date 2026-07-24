using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Orders;

namespace Genasys.Api.Services.Contracts;

public interface IOrderService
{
    Task<PagedResult<OrderResponse>> ListAsync(OrderListRequest request, CancellationToken cancellationToken);
    Task<OrderResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<OrderResponse> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken);
}
