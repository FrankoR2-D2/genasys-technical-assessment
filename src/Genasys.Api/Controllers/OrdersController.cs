using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Orders;
using Genasys.Api.Entities;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

/// <summary>
/// Order creation and lifecycle — the transactional flow the assessment is built around:
/// validate, check inventory, reserve, charge, and confirm or roll back.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>Lists orders, paginated and optionally filtered by status/customer.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List([FromQuery] OrderListRequest request, CancellationToken cancellationToken)
        => Ok(await orderService.ListAsync(request, cancellationToken));

    /// <summary>Gets a single order, including its line items and status history.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await orderService.GetByIdAsync(id, cancellationToken));

    /// <summary>
    /// Places an order: checks and reserves inventory, then processes payment.
    /// Confirms the order on success or releases inventory and cancels it on failure.
    /// Pass an <c>Idempotency-Key</c> header to safely retry after a dropped response.
    /// </summary>
    // Operational: any authenticated caller can place an order — this is
    // the transactional flow the whole assessment is built around.
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var response = await orderService.CreateAsync(request, idempotencyKey, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Manually overrides an order's status (e.g. marking it Shipped). Admin only —
    /// distinct from the automatic transitions the order-creation flow drives itself.
    /// </summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
        => Ok(await orderService.UpdateStatusAsync(id, request, cancellationToken));
}
