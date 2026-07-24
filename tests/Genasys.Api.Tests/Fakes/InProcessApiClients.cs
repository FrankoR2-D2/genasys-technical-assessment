using Genasys.Api.Clients;
using Genasys.Api.Common;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Contracts.Payments;
using Genasys.Api.Services.Contracts;

namespace Genasys.Api.Tests.Fakes;

// OrderService talks to Inventory/Payment over HTTP in production (per the
// spec's inter-service HTTP client requirement), which needs a real socket
// listening — awkward and fragile inside WebApplicationFactory's in-memory
// TestServer. These adapters call the real service classes in-process
// instead: the same business logic (locking, ledger, decline rules) runs
// against a real InMemory AppDbContext, with only the HTTP transport
// swapped out — that transport is ASP.NET Core/Polly library code, not
// something this project needs to re-verify.
public class InProcessInventoryApiClient(IInventoryService inventoryService) : IInventoryApiClient
{
    public Task<InventoryItemResponse> GetAsync(string productId, CancellationToken cancellationToken) =>
        inventoryService.GetByProductIdAsync(productId, cancellationToken);

    public Task<InventoryItemResponse> ReserveAsync(string productId, ReserveInventoryRequest request, CancellationToken cancellationToken) =>
        inventoryService.ReserveAsync(productId, request, cancellationToken);

    public Task<InventoryItemResponse> ReleaseAsync(string productId, ReleaseInventoryRequest request, CancellationToken cancellationToken) =>
        inventoryService.ReleaseAsync(productId, request, cancellationToken);
}

public class InProcessPaymentApiClient(IPaymentService paymentService) : IPaymentApiClient
{
    public Task<PaymentTransactionResponse> ProcessAsync(ProcessPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        paymentService.ProcessAsync(request, idempotencyKey, cancellationToken);
}

// Simulates a transport-level failure (the real service never even answers).
public class AlwaysThrowsPaymentApiClient : IPaymentApiClient
{
    public Task<PaymentTransactionResponse> ProcessAsync(ProcessPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        throw new UpstreamServiceUnavailableException("Payment service is unreachable.");
}

// Simulates the caller disconnecting mid-request: cancels the shared token
// (as ASP.NET Core would via HttpContext.RequestAborted) and throws the way
// a real cancelled HttpClient call would, so OrderService's compensation
// path has to run against an already-cancelled inbound token.
public class CancellingPaymentApiClient(CancellationTokenSource cancellationTokenSource) : IPaymentApiClient
{
    public Task<PaymentTransactionResponse> ProcessAsync(ProcessPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationTokenSource.Cancel();
        throw new OperationCanceledException(cancellationTokenSource.Token);
    }
}
