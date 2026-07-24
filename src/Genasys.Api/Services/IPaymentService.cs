using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Payments;

namespace Genasys.Api.Services;

public interface IPaymentService
{
    Task<PagedResult<PaymentTransactionResponse>> ListAsync(PaymentListRequest request, CancellationToken cancellationToken);
    Task<PaymentTransactionResponse> GetByIdAsync(Guid transactionId, CancellationToken cancellationToken);
    Task<PaymentTransactionResponse> ProcessAsync(ProcessPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken);
}
