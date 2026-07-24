using Genasys.Api.Contracts.Common;

namespace Genasys.Api.Contracts.Products;

public class CreateProductRequest
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? CategoryId { get; set; }

    // Inventory doesn't exist until a product does, so creation seeds it here.
    public int InitialQuantity { get; set; }
}

public class UpdateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? CategoryId { get; set; }
}

public class ProductListRequest : PagedRequest
{
    public Guid? CategoryId { get; set; }
}

public record ProductResponse(
    string ProductId,
    string Name,
    string Sku,
    string? Description,
    decimal UnitPrice,
    Guid? CategoryId,
    string? CategoryName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
