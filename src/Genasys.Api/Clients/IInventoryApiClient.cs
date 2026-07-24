using Genasys.Api.Contracts.Inventory;

namespace Genasys.Api.Clients;

public interface IInventoryApiClient
{
    Task<InventoryItemResponse> GetAsync(string productId, CancellationToken cancellationToken);
    Task<InventoryItemResponse> ReserveAsync(string productId, ReserveInventoryRequest request, CancellationToken cancellationToken);
    Task<InventoryItemResponse> ReleaseAsync(string productId, ReleaseInventoryRequest request, CancellationToken cancellationToken);
}
