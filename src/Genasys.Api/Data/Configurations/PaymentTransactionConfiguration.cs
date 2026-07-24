using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Genasys.Api.Data.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasKey(p => p.TransactionId);
        builder.HasIndex(p => p.OrderId);
        // Unique so a retried/duplicated request can't create two charges —
        // multiple nulls are still allowed since most transactions carry no key.
        builder.HasIndex(p => p.IdempotencyKey).IsUnique();
        builder.HasIndex(p => p.Status);
    }
}
