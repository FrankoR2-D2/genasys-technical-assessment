namespace Genasys.Api.Entities;

public class PaymentTransaction
{
    public Guid TransactionId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    // e.g. "**** 4242" — never the raw instrument.
    public string? MaskedReference { get; set; }
    public PaymentStatus Status { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
