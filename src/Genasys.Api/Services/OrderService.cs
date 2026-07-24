using Genasys.Api.Clients;
using Genasys.Api.Common;
using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Contracts.Orders;
using Genasys.Api.Contracts.Payments;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Genasys.Api.Services;

public class OrderService(
    AppDbContext db,
    IInventoryApiClient inventoryClient,
    IPaymentApiClient paymentClient,
    IInventoryService inventoryService,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<PagedResult<OrderResponse>> ListAsync(OrderListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Orders.Include(o => o.Items).Include(o => o.StatusHistory).AsQueryable();

        if (request.Status is not null)
        {
            query = query.Where(o => o.Status == request.Status);
        }

        if (request.CustomerId is not null)
        {
            query = query.Where(o => o.CustomerId == request.CustomerId);
        }

        var sort = SortSpec.Parse(request.Sort, "createdat");
        query = sort.Field.ToLowerInvariant() switch
        {
            "totalamount" => sort.Descending ? query.OrderByDescending(o => o.TotalAmount) : query.OrderBy(o => o.TotalAmount),
            "status" => sort.Descending ? query.OrderByDescending(o => o.Status) : query.OrderBy(o => o.Status),
            _ => sort.Descending ? query.OrderByDescending(o => o.CreatedAt) : query.OrderBy(o => o.CreatedAt)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<OrderResponse>.Create(orders.Select(ToResponse).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<OrderResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Order '{id}' was not found.");

        return ToResponse(order);
    }

    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingId = await db.Orders
                .Where(o => o.IdempotencyKey == idempotencyKey)
                .Select(o => (Guid?)o.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (existingId is not null)
            {
                logger.LogInformation("Idempotent replay of order creation {IdempotencyKey}", idempotencyKey);
                return await GetByIdAsync(existingId.Value, cancellationToken);
            }
        }

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException($"Customer '{request.CustomerId}' was not found.");

        // Prices always come from the catalog, never the client — an
        // OrderItemRequest only carries a productId and a quantity.
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, cancellationToken);

        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException($"Unknown product(s): {string.Join(", ", missing)}.");
        }

        var orderId = Guid.NewGuid();
        var items = request.Items.Select(i =>
        {
            var product = products[i.ProductId];
            return new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = product.ProductId,
                ProductName = product.Name,
                Quantity = i.Quantity,
                UnitPrice = product.UnitPrice
            };
        }).ToList();

        var totalAmount = items.Sum(i => i.Quantity * i.UnitPrice);

        // Availability check for every item before reserving anything —
        // never leave a partial reservation behind for a doomed order.
        foreach (var item in items)
        {
            var inventory = await inventoryClient.GetAsync(item.ProductId, cancellationToken);
            if (inventory.AvailableQuantity < item.Quantity)
            {
                throw new InsufficientInventoryException(
                    $"Only {inventory.AvailableQuantity} of '{item.ProductId}' available, {item.Quantity} requested.");
            }
        }

        var reserved = new List<OrderItem>();
        try
        {
            foreach (var item in items)
            {
                await inventoryClient.ReserveAsync(
                    item.ProductId,
                    new ReserveInventoryRequest { OrderId = orderId, Quantity = item.Quantity },
                    cancellationToken);
                reserved.Add(item);
            }
        }
        catch (DomainException)
        {
            await ReleaseAllAsync(orderId, reserved, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure reserving inventory for order {OrderId}", orderId);
            await ReleaseAllAsync(orderId, reserved, cancellationToken);
            throw new UpstreamServiceUnavailableException("Inventory service is unavailable.");
        }

        // Inventory is now held — the order is a real, auditable record from
        // this point on, whatever payment decides.
        var now = DateTime.UtcNow;
        var order = new Order
        {
            Id = orderId,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            IdempotencyKey = idempotencyKey,
            ShippingAddress = AddressMapper.ToEntity(request.ShippingAddress) ?? AddressMapper.Clone(customer.ShippingAddress),
            Items = items,
            TotalAmount = totalAmount,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Orders.Add(order);
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            FromStatus = null,
            ToStatus = OrderStatus.Pending,
            Reason = "Order created, inventory reserved.",
            ChangedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);

        PaymentTransactionResponse payment;
        try
        {
            payment = await paymentClient.ProcessAsync(
                new ProcessPaymentRequest
                {
                    OrderId = orderId,
                    Amount = totalAmount,
                    Method = request.PaymentMethod,
                    InstrumentReference = request.PaymentInstrumentReference
                },
                idempotencyKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payment service unavailable for order {OrderId}", orderId);
            await ReleaseAllAsync(orderId, items, cancellationToken);
            await TransitionAsync(order, OrderStatus.Cancelled, "Payment service unavailable.", cancellationToken);
            throw new UpstreamServiceUnavailableException($"Payment service is unavailable. Order '{orderId}' was cancelled.");
        }

        if (Enum.Parse<PaymentStatus>(payment.Status) == PaymentStatus.Completed)
        {
            await inventoryService.ConsumeReservationsAsync(orderId, cancellationToken);
            await TransitionAsync(order, OrderStatus.Confirmed, "Payment completed.", cancellationToken);
            return await GetByIdAsync(orderId, cancellationToken);
        }

        await ReleaseAllAsync(orderId, items, cancellationToken);
        await TransitionAsync(order, OrderStatus.Cancelled, "Payment declined.", cancellationToken);
        throw new PaymentFailedException($"Payment was declined for order '{orderId}'.");
    }

    public async Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Order '{id}' was not found.");

        if (!IsValidTransition(order.Status, request.Status))
        {
            throw new ConflictException($"Cannot transition order from '{order.Status}' to '{request.Status}'.");
        }

        await TransitionAsync(order, request.Status, request.Reason ?? "Manual status update.", cancellationToken);
        return ToResponse(order);
    }

    private async Task ReleaseAllAsync(Guid orderId, IEnumerable<OrderItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            try
            {
                await inventoryClient.ReleaseAsync(
                    item.ProductId,
                    new ReleaseInventoryRequest { OrderId = orderId, Quantity = item.Quantity },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Already unwinding a failure — log and keep releasing the
                // rest rather than letting a secondary error mask the first.
                logger.LogError(ex, "Failed to release {Quantity} of {ProductId} for order {OrderId} during rollback",
                    item.Quantity, item.ProductId, orderId);
            }
        }
    }

    private async Task TransitionAsync(Order order, OrderStatus newStatus, string reason, CancellationToken cancellationToken)
    {
        var previous = order.Status;
        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = previous,
            ToStatus = newStatus,
            Reason = reason,
            ChangedAt = order.UpdatedAt
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsValidTransition(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Pending, OrderStatus.Confirmed) => true,
        (OrderStatus.Pending, OrderStatus.Cancelled) => true,
        (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
        (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
        _ => false
    };

    private static OrderItemResponse ToResponse(OrderItem item) =>
        new(item.ProductId, item.ProductName, item.Quantity, item.UnitPrice, item.Quantity * item.UnitPrice);

    private static OrderStatusHistoryEntry ToResponse(OrderStatusHistory history) =>
        new(history.FromStatus?.ToString(), history.ToStatus.ToString(), history.Reason, history.ChangedAt);

    private static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.CustomerId,
        order.CustomerName,
        order.Items.Select(ToResponse).ToList(),
        order.TotalAmount,
        order.Status.ToString(),
        AddressMapper.ToResponse(order.ShippingAddress),
        order.StatusHistory.OrderBy(h => h.ChangedAt).Select(ToResponse).ToList(),
        order.CreatedAt,
        order.UpdatedAt);
}
