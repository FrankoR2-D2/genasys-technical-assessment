using Genasys.Api.Contracts.Common;

namespace Genasys.Api.Contracts.Inventory;

public class ReserveInventoryRequest
{
    public Guid OrderId { get; set; }
    public int Quantity { get; set; }
}

public class ReleaseInventoryRequest
{
    public Guid OrderId { get; set; }
    public int Quantity { get; set; }
}

public class InventoryListRequest : PagedRequest
{
    public bool LowStockOnly { get; set; }
    public int LowStockThreshold { get; set; } = 10;
}

public record InventoryItemResponse(
    string ProductId,
    int TotalQuantity,
    int AvailableQuantity,
    int ReservedQuantity);
