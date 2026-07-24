using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Payments;
using Genasys.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PaymentTransactionResponse>>> List([FromQuery] PaymentListRequest request, CancellationToken cancellationToken)
        => Ok(await paymentService.ListAsync(request, cancellationToken));

    [HttpGet("{transactionId:guid}")]
    public async Task<ActionResult<PaymentTransactionResponse>> GetById(Guid transactionId, CancellationToken cancellationToken)
        => Ok(await paymentService.GetByIdAsync(transactionId, cancellationToken));

    [HttpPost("process")]
    public async Task<ActionResult<PaymentTransactionResponse>> Process(
        ProcessPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
        => Ok(await paymentService.ProcessAsync(request, idempotencyKey, cancellationToken));
}
