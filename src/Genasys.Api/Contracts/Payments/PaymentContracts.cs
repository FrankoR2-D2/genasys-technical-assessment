using Genasys.Api.Contracts.Common;
using Genasys.Api.Entities;

namespace Genasys.Api.Contracts.Payments;

public class ProcessPaymentRequest
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    // e.g. a client-supplied "last 4 digits" for display — never a full card/account number.
    public string? InstrumentReference { get; set; }
}

public class PaymentListRequest : PagedRequest
{
    public PaymentStatus? Status { get; set; }
    public Guid? OrderId { get; set; }
}

public record PaymentTransactionResponse(
    Guid TransactionId,
    Guid OrderId,
    decimal Amount,
    string Method,
    string? MaskedReference,
    string Status,
    DateTime? ProcessedAt,
    DateTime CreatedAt);
