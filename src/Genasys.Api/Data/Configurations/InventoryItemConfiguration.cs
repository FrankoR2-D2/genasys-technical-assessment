using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Genasys.Api.Data.Configurations;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.HasKey(i => i.ProductId);
        builder.Property(i => i.RowVersion).IsConcurrencyToken();

        // No formal FK to Product: services look up inventory by ProductId
        // directly and validate existence themselves, so a relationship
        // navigation would only exist to trip the soft-delete query-filter
        // warning for a path nothing actually uses.
        builder.HasMany(i => i.Reservations)
            .WithOne()
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
