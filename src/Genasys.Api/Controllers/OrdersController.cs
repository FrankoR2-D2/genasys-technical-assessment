using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Orders;
using Genasys.Api.Entities;
using Genasys.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List([FromQuery] OrderListRequest request, CancellationToken cancellationToken)
        => Ok(await orderService.ListAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await orderService.GetByIdAsync(id, cancellationToken));

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

    // Administrative override (e.g. manually marking Shipped) — distinct
    // from the automatic transitions the order flow drives itself.
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
        => Ok(await orderService.UpdateStatusAsync(id, request, cancellationToken));
}
