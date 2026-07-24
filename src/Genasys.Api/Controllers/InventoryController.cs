using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

/// <summary>
/// Stock levels and reservations per product. Reserve/release are called by
/// <c>OrderService</c> as part of the order flow, but are exposed as their own
/// endpoints per the assessment's inventory API surface.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController(IInventoryService inventoryService) : ControllerBase
{
    /// <summary>Lists inventory items, paginated and optionally filtered to low stock.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<InventoryItemResponse>>> List([FromQuery] InventoryListRequest request, CancellationToken cancellationToken)
        => Ok(await inventoryService.ListAsync(request, cancellationToken));

    /// <summary>Gets current available/reserved/total quantity for a product.</summary>
    [HttpGet("{productId}")]
    public async Task<ActionResult<InventoryItemResponse>> GetByProductId(string productId, CancellationToken cancellationToken)
        => Ok(await inventoryService.GetByProductIdAsync(productId, cancellationToken));

    /// <summary>
    /// Reserves stock for an order. Retrying with the same order id and quantity is
    /// safe — an existing active reservation is returned unchanged rather than duplicated.
    /// </summary>
    // Operational, not administrative: any authenticated caller — this is
    // what OrderService calls on behalf of whoever is placing the order.
    [HttpPost("{productId}/reserve")]
    public async Task<ActionResult<InventoryItemResponse>> Reserve(string productId, ReserveInventoryRequest request, CancellationToken cancellationToken)
        => Ok(await inventoryService.ReserveAsync(productId, request, cancellationToken));

    /// <summary>Releases a previously-made reservation, returning stock to available.</summary>
    [HttpPost("{productId}/release")]
    public async Task<ActionResult<InventoryItemResponse>> Release(string productId, ReleaseInventoryRequest request, CancellationToken cancellationToken)
        => Ok(await inventoryService.ReleaseAsync(productId, request, cancellationToken));
}
