using FluentAssertions;
using Genasys.Api.Common;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Genasys.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Genasys.Api.Tests;

public class InventoryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly InventoryService _inventoryService;

    public InventoryServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _inventoryService = new InventoryService(_db, new KeyedLockProvider(), NullLogger<InventoryService>.Instance);

        _db.InventoryItems.Add(new InventoryItem { ProductId = "P1", TotalQuantity = 10, AvailableQuantity = 10, ReservedQuantity = 0 });
        _db.SaveChanges();
    }

    [Fact]
    public async Task ReserveAsync_InsufficientStock_ThrowsAndLeavesCountersUnchanged()
    {
        var act = () => _inventoryService.ReserveAsync("P1", new ReserveInventoryRequest { OrderId = Guid.NewGuid(), Quantity = 11 }, CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientInventoryException>();

        var item = await _db.InventoryItems.SingleAsync(i => i.ProductId == "P1");
        item.AvailableQuantity.Should().Be(10);
    }

    [Fact]
    public async Task ReserveThenRelease_RoundTripsCountersExactly()
    {
        var orderId = Guid.NewGuid();
        await _inventoryService.ReserveAsync("P1", new ReserveInventoryRequest { OrderId = orderId, Quantity = 4 }, CancellationToken.None);
        await _inventoryService.ReleaseAsync("P1", new ReleaseInventoryRequest { OrderId = orderId, Quantity = 4 }, CancellationToken.None);

        var item = await _db.InventoryItems.SingleAsync(i => i.ProductId == "P1");
        item.AvailableQuantity.Should().Be(10);
        item.ReservedQuantity.Should().Be(0);
        item.TotalQuantity.Should().Be(10);
    }

    [Fact]
    public async Task ConcurrentReserve_NeverOversells()
    {
        // Ten units on hand, two competing reservations for seven units each
        // — the keyed semaphore must serialize these so only one can win;
        // the loser sees a fresh (post-lock) read and correctly fails rather
        // than both succeeding against a stale availability snapshot.
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        var taskA = _inventoryService.ReserveAsync("P1", new ReserveInventoryRequest { OrderId = orderA, Quantity = 7 }, CancellationToken.None);
        var taskB = _inventoryService.ReserveAsync("P1", new ReserveInventoryRequest { OrderId = orderB, Quantity = 7 }, CancellationToken.None);

        var results = await Task.WhenAll(taskA.ContinueWith(TranslateOutcome), taskB.ContinueWith(TranslateOutcome));

        results.Count(r => r == "ok").Should().Be(1);
        results.Count(r => r == "insufficient").Should().Be(1);

        var item = await _db.InventoryItems.SingleAsync(i => i.ProductId == "P1");
        item.AvailableQuantity.Should().Be(3);
        item.ReservedQuantity.Should().Be(7);
        item.AvailableQuantity.Should().BeGreaterThanOrEqualTo(0);
    }

    private static string TranslateOutcome(Task<InventoryItemResponse> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return "ok";
        }

        return task.Exception?.InnerException is InsufficientInventoryException ? "insufficient" : "unexpected-error";
    }

    public void Dispose() => _db.Dispose();
}
