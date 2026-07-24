using Genasys.Api.Common;
using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Payments;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Genasys.Api.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Genasys.Api.Services;

public class PaymentService(AppDbContext db, ILogger<PaymentService> logger) : IPaymentService
{
    public async Task<PagedResult<PaymentTransactionResponse>> ListAsync(PaymentListRequest request, CancellationToken cancellationToken)
    {
        var query = db.PaymentTransactions.AsQueryable();

        if (request.Status is not null)
        {
            query = query.Where(p => p.Status == request.Status);
        }

        if (request.OrderId is not null)
        {
            query = query.Where(p => p.OrderId == request.OrderId);
        }

        var sort = SortSpec.Parse(request.Sort, "createdat");
        query = sort.Field.ToLowerInvariant() switch
        {
            "amount" => sort.Descending ? query.OrderByDescending(p => p.Amount) : query.OrderBy(p => p.Amount),
            "status" => sort.Descending ? query.OrderByDescending(p => p.Status) : query.OrderBy(p => p.Status),
            _ => sort.Descending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<PaymentTransactionResponse>.Create(transactions.Select(ToResponse).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<PaymentTransactionResponse> GetByIdAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await db.PaymentTransactions.SingleOrDefaultAsync(p => p.TransactionId == transactionId, cancellationToken)
            ?? throw new NotFoundException($"Payment transaction '{transactionId}' was not found.");
        return ToResponse(transaction);
    }

    public async Task<PaymentTransactionResponse> ProcessAsync(ProcessPaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.PaymentTransactions.SingleOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                logger.LogInformation("Idempotent replay of payment request {IdempotencyKey}", idempotencyKey);
                return ToResponse(existing);
            }
        }

        var now = DateTime.UtcNow;
        var transaction = new PaymentTransaction
        {
            TransactionId = Guid.NewGuid(),
            OrderId = request.OrderId,
            Amount = request.Amount,
            Method = request.Method,
            MaskedReference = MaskReference(request.InstrumentReference),
            IdempotencyKey = idempotencyKey,
            CreatedAt = now
        };

        // Simulated gateway: pass instrumentReference "DECLINE" to exercise
        // the failure path deterministically (no real processor involved).
        var declined = string.Equals(request.InstrumentReference, "DECLINE", StringComparison.OrdinalIgnoreCase);
        transaction.Status = declined ? PaymentStatus.Failed : PaymentStatus.Completed;
        transaction.ProcessedAt = DateTime.UtcNow;

        db.PaymentTransactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Payment {TransactionId} for order {OrderId}: {Status}", transaction.TransactionId, request.OrderId, transaction.Status);
        return ToResponse(transaction);
    }

    private static string? MaskReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var visible = reference.Length > 4 ? reference[^4..] : reference;
        return $"**** {visible}";
    }

    private static PaymentTransactionResponse ToResponse(PaymentTransaction transaction) => new(
        transaction.TransactionId,
        transaction.OrderId,
        transaction.Amount,
        transaction.Method.ToString(),
        transaction.MaskedReference,
        transaction.Status.ToString(),
        transaction.ProcessedAt,
        transaction.CreatedAt);
}
