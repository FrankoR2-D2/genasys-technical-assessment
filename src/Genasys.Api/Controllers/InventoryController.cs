using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController(IInventoryService inventoryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<InventoryItemResponse>>> List([FromQuery] InventoryListRequest request, CancellationToken cancellationToken)
        => Ok(await inventoryService.ListAsync(request, cancellationToken));

    [HttpGet("{productId}")]
    public async Task<ActionResult<InventoryItemResponse>> GetByProductId(string productId, CancellationToken cancellationToken)
        => Ok(await inventoryService.GetByProductIdAsync(productId, cancellationToken));

    // Operational, not administrative: any authenticated caller — this is
    // what OrderService calls on behalf of whoever is placing the order.
    [HttpPost("{productId}/reserve")]
    public async Task<ActionResult<InventoryItemResponse>> Reserve(string productId, ReserveInventoryRequest request, CancellationToken cancellationToken)
        => Ok(await inventoryService.ReserveAsync(productId, request, cancellationToken));

    [HttpPost("{productId}/release")]
    public async Task<ActionResult<InventoryItemResponse>> Release(string productId, ReleaseInventoryRequest request, CancellationToken cancellationToken)
        => Ok(await inventoryService.ReleaseAsync(productId, request, cancellationToken));
}
