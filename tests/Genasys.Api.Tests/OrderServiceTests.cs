using FluentAssertions;
using Genasys.Api.Clients;
using Genasys.Api.Common;
using Genasys.Api.Contracts.Orders;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Genasys.Api.Services;
using Genasys.Api.Services.Contracts;
using Genasys.Api.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Genasys.Api.Tests;

public class OrderServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IOrderService _orderService;
    private readonly Guid _customerId = Guid.NewGuid();

    public OrderServiceTests()
    {
        _db = TestDbContextFactory.Create();

        var inventoryService = new InventoryService(_db, new KeyedLockProvider(), NullLogger<InventoryService>.Instance);
        var paymentService = new PaymentService(_db, NullLogger<PaymentService>.Instance);

        IInventoryApiClient inventoryClient = new InProcessInventoryApiClient(inventoryService);
        IPaymentApiClient paymentClient = new InProcessPaymentApiClient(paymentService);

        _orderService = new OrderService(_db, inventoryClient, paymentClient, inventoryService, NullLogger<OrderService>.Instance);

        SeedCatalog();
    }

    private void SeedCatalog()
    {
        var now = DateTime.UtcNow;
        _db.Customers.Add(new Customer { Id = _customerId, Name = "Test Customer", Email = "test@example.com", CreatedAt = now, UpdatedAt = now });
        _db.Products.Add(new Product { ProductId = "P1", Name = "Widget", Sku = "P1", UnitPrice = 10m, CreatedAt = now, UpdatedAt = now });
        _db.InventoryItems.Add(new InventoryItem { ProductId = "P1", TotalQuantity = 5, AvailableQuantity = 5, ReservedQuantity = 0 });
        _db.SaveChanges();
    }

    [Fact]
    public async Task CreateAsync_HappyPath_ConfirmsOrderAndDebitsInventory()
    {
        var request = new CreateOrderRequest { CustomerId = _customerId, Items = [new CreateOrderItemRequest { ProductId = "P1", Quantity = 2 }] };

        var response = await _orderService.CreateAsync(request, null, CancellationToken.None);

        response.Status.Should().Be("Confirmed");
        response.TotalAmount.Should().Be(20m);
        response.StatusHistory.Should().HaveCount(2);

        var inventory = await _db.InventoryItems.SingleAsync(i => i.ProductId == "P1");
        inventory.TotalQuantity.Should().Be(3);
        inventory.AvailableQuantity.Should().Be(3);
        inventory.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_InsufficientInventory_ThrowsAndCreatesNoOrder()
    {
        var request = new CreateOrderRequest { CustomerId = _customerId, Items = [new CreateOrderItemRequest { ProductId = "P1", Quantity = 999 }] };

        var act = () => _orderService.CreateAsync(request, null, CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientInventoryException>();
        (await _db.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_PaymentDeclined_CancelsOrderAndReleasesInventory()
    {
        var request = new CreateOrderRequest
        {
            CustomerId = _customerId,
            Items = [new CreateOrderItemRequest { ProductId = "P1", Quantity = 2 }],
            PaymentInstrumentReference = "DECLINE"
        };

        var act = () => _orderService.CreateAsync(request, null, CancellationToken.None);

        await act.Should().ThrowAsync<PaymentFailedException>();

        var order = await _db.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.Cancelled);

        var inventory = await _db.InventoryItems.SingleAsync(i => i.ProductId == "P1");
        inventory.AvailableQuantity.Should().Be(5);
        inventory.ReservedQuantity.Should().Be(0);
        inventory.TotalQuantity.Should().Be(5);
    }

    [Fact]
    public async Task CreateAsync_PaymentServiceUnavailable_CancelsOrderAndReleasesInventory()
    {
        var inventoryService = new InventoryService(_db, new KeyedLockProvider(), NullLogger<InventoryService>.Instance);
        var orderServiceWithBrokenPayment = new OrderService(
            _db,
            new InProcessInventoryApiClient(inventoryService),
            new AlwaysThrowsPaymentApiClient(),
            inventoryService,
            NullLogger<OrderService>.Instance);

        var request = new CreateOrderRequest { CustomerId = _customerId, Items = [new CreateOrderItemRequest { ProductId = "P1", Quantity = 1 }] };

        var act = () => orderServiceWithBrokenPayment.CreateAsync(request, null, CancellationToken.None);

        await act.Should().ThrowAsync<UpstreamServiceUnavailableException>();

        var order = await _db.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.Cancelled);

        var inventory = await _db.InventoryItems.SingleAsync(i => i.ProductId == "P1");
        inventory.AvailableQuantity.Should().Be(5);
    }

    [Fact]
    public async Task CreateAsync_DuplicateIdempotencyKey_ReturnsSameOrderAndDebitsOnce()
    {
        var request = new CreateOrderRequest { CustomerId = _customerId, Items = [new CreateOrderItemRequest { ProductId = "P1", Quantity = 1 }] };

        var first = await _orderService.CreateAsync(request, "key-1", CancellationToken.None);
        var second = await _orderService.CreateAsync(request, "key-1", CancellationToken.None);

        second.Id.Should().Be(first.Id);
        (await _db.Orders.CountAsync()).Should().Be(1);

        var inventory = await _db.InventoryItems.SingleAsync(i => i.ProductId == "P1");
        inventory.TotalQuantity.Should().Be(4);
    }

    [Fact]
    public async Task CreateAsync_UnknownProduct_ThrowsNotFound()
    {
        var request = new CreateOrderRequest { CustomerId = _customerId, Items = [new CreateOrderItemRequest { ProductId = "does-not-exist", Quantity = 1 }] };

        var act = () => _orderService.CreateAsync(request, null, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateStatusAsync_InvalidTransition_ThrowsConflict()
    {
        var request = new CreateOrderRequest { CustomerId = _customerId, Items = [new CreateOrderItemRequest { ProductId = "P1", Quantity = 1 }] };
        var order = await _orderService.CreateAsync(request, null, CancellationToken.None);

        var act = () => _orderService.UpdateStatusAsync(order.Id, new UpdateOrderStatusRequest { Status = OrderStatus.Pending }, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    public void Dispose() => _db.Dispose();
}
