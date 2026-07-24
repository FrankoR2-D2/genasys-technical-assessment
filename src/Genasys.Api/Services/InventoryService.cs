using Genasys.Api.Common;
using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Genasys.Api.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Genasys.Api.Services;

public class InventoryService(AppDbContext db, KeyedLockProvider lockProvider, ILogger<InventoryService> logger) : IInventoryService
{
    public async Task<PagedResult<InventoryItemResponse>> ListAsync(InventoryListRequest request, CancellationToken cancellationToken)
    {
        var query = db.InventoryItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(i => i.ProductId.Contains(term));
        }

        if (request.LowStockOnly)
        {
            query = query.Where(i => i.AvailableQuantity < request.LowStockThreshold);
        }

        var sort = SortSpec.Parse(request.Sort, "productid");
        query = sort.Field.ToLowerInvariant() switch
        {
            "available" or "availablequantity" => sort.Descending ? query.OrderByDescending(i => i.AvailableQuantity) : query.OrderBy(i => i.AvailableQuantity),
            "reserved" or "reservedquantity" => sort.Descending ? query.OrderByDescending(i => i.ReservedQuantity) : query.OrderBy(i => i.ReservedQuantity),
            _ => sort.Descending ? query.OrderByDescending(i => i.ProductId) : query.OrderBy(i => i.ProductId)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip(request.EffectiveSkip)
            .Take(request.EffectiveTake)
            .ToListAsync(cancellationToken);

        return PagedResult<InventoryItemResponse>.Create(items.Select(ToResponse).ToList(), request.EffectivePage, request.EffectiveTake, totalCount);
    }

    public async Task<InventoryItemResponse> GetByProductIdAsync(string productId, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.SingleOrDefaultAsync(i => i.ProductId == productId, cancellationToken)
            ?? throw new NotFoundException($"Inventory for product '{productId}' was not found.");
        return ToResponse(item);
    }

    public async Task<InventoryItemResponse> ReserveAsync(string productId, ReserveInventoryRequest request, CancellationToken cancellationToken)
    {
        await using var _ = await lockProvider.AcquireAsync(productId, cancellationToken);

        var item = await db.InventoryItems.SingleOrDefaultAsync(i => i.ProductId == productId, cancellationToken)
            ?? throw new NotFoundException($"Inventory for product '{productId}' was not found.");

        if (item.AvailableQuantity < request.Quantity)
        {
            throw new InsufficientInventoryException(
                $"Only {item.AvailableQuantity} of '{productId}' available, {request.Quantity} requested.");
        }

        item.AvailableQuantity -= request.Quantity;
        item.ReservedQuantity += request.Quantity;

        db.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            OrderId = request.OrderId,
            Quantity = request.Quantity,
            Status = ReservationStatus.Active,
            ReservedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Reserved {Quantity} of {ProductId} for order {OrderId}", request.Quantity, productId, request.OrderId);

        return ToResponse(item);
    }

    public async Task<InventoryItemResponse> ReleaseAsync(string productId, ReleaseInventoryRequest request, CancellationToken cancellationToken)
    {
        await using var _ = await lockProvider.AcquireAsync(productId, cancellationToken);

        var item = await db.InventoryItems.SingleOrDefaultAsync(i => i.ProductId == productId, cancellationToken)
            ?? throw new NotFoundException($"Inventory for product '{productId}' was not found.");

        var reservation = await db.InventoryReservations
            .Where(r => r.ProductId == productId && r.OrderId == request.OrderId && r.Status == ReservationStatus.Active)
            .OrderBy(r => r.ReservedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"No active reservation for product '{productId}' on order '{request.OrderId}'.");

        if (reservation.Quantity != request.Quantity)
        {
            throw new ConflictException(
                $"Release quantity {request.Quantity} does not match reserved quantity {reservation.Quantity} for product '{productId}'.");
        }

        reservation.Status = ReservationStatus.Released;
        reservation.ReleasedAt = DateTime.UtcNow;

        item.AvailableQuantity += request.Quantity;
        item.ReservedQuantity -= request.Quantity;

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Released {Quantity} of {ProductId} for order {OrderId}", request.Quantity, productId, request.OrderId);

        return ToResponse(item);
    }

    public async Task ConsumeReservationsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var reservations = await db.InventoryReservations
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            await using var _ = await lockProvider.AcquireAsync(reservation.ProductId, cancellationToken);

            var item = await db.InventoryItems.SingleAsync(i => i.ProductId == reservation.ProductId, cancellationToken);

            // Goods physically leave the warehouse: Available was already
            // debited at reserve time, so only Total and Reserved move now.
            item.TotalQuantity -= reservation.Quantity;
            item.ReservedQuantity -= reservation.Quantity;
            reservation.Status = ReservationStatus.Consumed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static InventoryItemResponse ToResponse(InventoryItem item) =>
        new(item.ProductId, item.TotalQuantity, item.AvailableQuantity, item.ReservedQuantity);
}
