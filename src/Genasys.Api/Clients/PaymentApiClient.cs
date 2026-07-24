using System.Net.Http.Json;
using Genasys.Api.Common;
using Genasys.Api.Contracts.Payments;

namespace Genasys.Api.Clients;

public class PaymentApiClient(HttpClient httpClient) : IPaymentApiClient
{
    public async Task<PaymentTransactionResponse> ProcessAsync(ProcessPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/payments/process")
        {
            Content = JsonContent.Create(request)
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var response = await httpClient.SendAsync(message, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<PaymentTransactionResponse>(cancellationToken))!;
        }

        // A non-success response here means the payment service itself is
        // unreachable/misbehaving, not that the payment was declined — a
        // decline is a 200 with Status "failed", handled by the caller.
        throw new UpstreamServiceUnavailableException($"Payment service returned {(int)response.StatusCode}.");
    }
}
