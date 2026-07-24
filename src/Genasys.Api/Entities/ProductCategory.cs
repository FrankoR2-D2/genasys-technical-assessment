namespace Genasys.Api.Entities;

public class ProductCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentCategoryId { get; set; }

    public ProductCategory? ParentCategory { get; set; }
    public List<Product> Products { get; set; } = [];
}
