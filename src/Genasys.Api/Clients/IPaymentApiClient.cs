using Genasys.Api.Contracts.Payments;

namespace Genasys.Api.Clients;

public interface IPaymentApiClient
{
    Task<PaymentTransactionResponse> ProcessAsync(ProcessPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken);
}
