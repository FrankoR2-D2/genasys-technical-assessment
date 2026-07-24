using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Products;

namespace Genasys.Api.Services.Contracts;

public interface IProductService
{
    Task<PagedResult<ProductResponse>> ListAsync(ProductListRequest request, CancellationToken cancellationToken);
    Task<ProductResponse> GetByIdAsync(string productId, CancellationToken cancellationToken);
    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);
    Task<ProductResponse> UpdateAsync(string productId, UpdateProductRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(string productId, CancellationToken cancellationToken);
}
