using FluentAssertions;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Genasys.Api.Tests;

// Proves the RowVersion optimistic-concurrency mechanism itself (AppDbContext's
// BumpRowVersions override + IsConcurrencyToken() configuration) actually
// detects a stale write, independent of the KeyedLockProvider locks that
// normally prevent this race from occurring in practice. Two separate
// DbContext instances against the same InMemory database name simulate two
// concurrent requests, mirroring how ASP.NET Core hands each request its own
// scoped context.
public class ConcurrencyTests
{
    [Fact]
    public async Task ConcurrentInventoryItemUpdates_SecondSaveThrowsConcurrencyException()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.InventoryItems.Add(new InventoryItem { ProductId = "CONC-1", TotalQuantity = 10, AvailableQuantity = 10, ReservedQuantity = 0 });
            await seedContext.SaveChangesAsync();
        }

        await using var contextA = new AppDbContext(options);
        await using var contextB = new AppDbContext(options);

        var itemA = await contextA.InventoryItems.SingleAsync(i => i.ProductId == "CONC-1");
        var itemB = await contextB.InventoryItems.SingleAsync(i => i.ProductId == "CONC-1");

        itemA.AvailableQuantity -= 1;
        await contextA.SaveChangesAsync();

        itemB.AvailableQuantity -= 1;
        var act = () => contextB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "contextB's tracked RowVersion is now stale relative to contextA's committed write");
    }

    [Fact]
    public async Task ConcurrentOrderUpdates_SecondSaveThrowsConcurrencyException()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var orderId = Guid.NewGuid();

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.Customers.Add(new Customer { Id = Guid.NewGuid(), Name = "C", Email = "c@example.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            seedContext.Orders.Add(new Order
            {
                Id = orderId,
                CustomerId = Guid.NewGuid(),
                CustomerName = "C",
                TotalAmount = 10,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await seedContext.SaveChangesAsync();
        }

        await using var contextA = new AppDbContext(options);
        await using var contextB = new AppDbContext(options);

        var orderA = await contextA.Orders.SingleAsync(o => o.Id == orderId);
        var orderB = await contextB.Orders.SingleAsync(o => o.Id == orderId);

        orderA.Status = OrderStatus.Confirmed;
        await contextA.SaveChangesAsync();

        orderB.Status = OrderStatus.Cancelled;
        var act = () => contextB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
