using Genasys.Api.Common;
using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Products;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Genasys.Api.Services;

public class ProductService(AppDbContext db, IMemoryCache cache, ILogger<ProductService> logger) : IProductService
{
    // Only single-item lookups are cached — a catalog read is far hotter than
    // a list/search, and caching every distinct page/search/sort combination
    // wouldn't pay for itself at this scale.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static string CacheKey(string productId) => $"product:{productId}";

    public async Task<PagedResult<ProductResponse>> ListAsync(ProductListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Products.Include(p => p.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Sku.Contains(term));
        }

        if (request.CategoryId is not null)
        {
            query = query.Where(p => p.CategoryId == request.CategoryId);
        }

        var sort = SortSpec.Parse(request.Sort, "name");
        query = sort.Field.ToLowerInvariant() switch
        {
            "unitprice" => sort.Descending ? query.OrderByDescending(p => p.UnitPrice) : query.OrderBy(p => p.UnitPrice),
            "createdat" => sort.Descending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => sort.Descending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var products = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<ProductResponse>.Create(products.Select(ToResponse).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<ProductResponse> GetByIdAsync(string productId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey(productId), out ProductResponse? cached) && cached is not null)
        {
            return cached;
        }

        var product = await db.Products.Include(p => p.Category)
            .SingleOrDefaultAsync(p => p.ProductId == productId, cancellationToken)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        var response = ToResponse(product);
        cache.Set(CacheKey(productId), response, CacheDuration);
        return response;
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: ProductId is the primary key, so a soft-deleted
        // row still occupies it — a normal (filtered) check would miss that.
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.ProductId == request.ProductId, cancellationToken))
        {
            throw new ConflictException($"Product '{request.ProductId}' already exists.");
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            ProductId = request.ProductId,
            Name = request.Name,
            Sku = request.Sku,
            Description = request.Description,
            UnitPrice = request.UnitPrice,
            CategoryId = request.CategoryId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Products.Add(product);

        db.InventoryItems.Add(new InventoryItem
        {
            ProductId = product.ProductId,
            TotalQuantity = request.InitialQuantity,
            AvailableQuantity = request.InitialQuantity,
            ReservedQuantity = 0
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created product {ProductId}", product.ProductId);

        return await GetByIdAsync(product.ProductId, cancellationToken);
    }

    public async Task<ProductResponse> UpdateAsync(string productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.ProductId == productId, cancellationToken)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        product.Name = request.Name;
        product.Description = request.Description;
        product.UnitPrice = request.UnitPrice;
        product.CategoryId = request.CategoryId;
        product.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey(productId));

        return await GetByIdAsync(productId, cancellationToken);
    }

    public async Task DeleteAsync(string productId, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.ProductId == productId, cancellationToken)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey(productId));
        logger.LogInformation("Soft-deleted product {ProductId}", productId);
    }

    private static ProductResponse ToResponse(Product product) => new(
        product.ProductId,
        product.Name,
        product.Sku,
        product.Description,
        product.UnitPrice,
        product.CategoryId,
        product.Category?.Name,
        product.CreatedAt,
        product.UpdatedAt);
}
