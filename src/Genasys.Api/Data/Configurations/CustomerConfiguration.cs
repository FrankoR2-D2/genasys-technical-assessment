using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Genasys.Api.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Email).IsRequired().HasMaxLength(320);
        builder.HasIndex(c => c.Email);

        builder.OwnsOne(c => c.ShippingAddress);
        builder.OwnsOne(c => c.BillingAddress);

        builder.HasQueryFilter(c => !c.IsDeleted);

        // No formal FK to Order: Order snapshots CustomerName at creation
        // time and services look up customers by CustomerId directly, so a
        // relationship navigation would only exist to trip the same
        // soft-delete query-filter issue already worked around on Product.
    }
}
