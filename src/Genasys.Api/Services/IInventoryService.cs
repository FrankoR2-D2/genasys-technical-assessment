using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Inventory;

namespace Genasys.Api.Services;

public interface IInventoryService
{
    Task<PagedResult<InventoryItemResponse>> ListAsync(InventoryListRequest request, CancellationToken cancellationToken);
    Task<InventoryItemResponse> GetByProductIdAsync(string productId, CancellationToken cancellationToken);
    Task<InventoryItemResponse> ReserveAsync(string productId, ReserveInventoryRequest request, CancellationToken cancellationToken);
    Task<InventoryItemResponse> ReleaseAsync(string productId, ReleaseInventoryRequest request, CancellationToken cancellationToken);

    // Not an HTTP-exposed operation — called in-process once an order's
    // payment succeeds, to finalize the reservations the order flow made
    // over HTTP into a permanent stock decrement.
    Task ConsumeReservationsAsync(Guid orderId, CancellationToken cancellationToken);
}
