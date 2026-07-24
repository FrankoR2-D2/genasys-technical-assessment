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
        builder.HasIndex(p => p.IdempotencyKey);
        builder.HasIndex(p => p.Status);
    }
}
