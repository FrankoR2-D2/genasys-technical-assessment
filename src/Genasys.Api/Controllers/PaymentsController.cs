using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Payments;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

/// <summary>
/// Simulated payment gateway. There is no real processor — pass
/// <c>paymentInstrumentReference: "DECLINE"</c> to deterministically exercise the
/// failure path; anything else succeeds.
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    /// <summary>Lists payment transactions, paginated and optionally filtered by status/order.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<PaymentTransactionResponse>>> List([FromQuery] PaymentListRequest request, CancellationToken cancellationToken)
        => Ok(await paymentService.ListAsync(request, cancellationToken));

    /// <summary>Gets a single payment transaction by id.</summary>
    [HttpGet("{transactionId:guid}")]
    public async Task<ActionResult<PaymentTransactionResponse>> GetById(Guid transactionId, CancellationToken cancellationToken)
        => Ok(await paymentService.GetByIdAsync(transactionId, cancellationToken));

    /// <summary>
    /// Processes a payment against the simulated gateway. Pass an <c>Idempotency-Key</c>
    /// header to safely retry without double-charging.
    /// </summary>
    [HttpPost("process")]
    public async Task<ActionResult<PaymentTransactionResponse>> Process(
        ProcessPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
        => Ok(await paymentService.ProcessAsync(request, idempotencyKey, cancellationToken));
}
