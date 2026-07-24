using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Products;
using Genasys.Api.Entities;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

/// <summary>Product catalog — not part of the spec's required models, exposed as a full CRUD resource.</summary>
[ApiController]
[Route("api/products")]
public class ProductsController(IProductService productService) : ControllerBase
{
    /// <summary>Lists products, paginated and searchable by name/SKU, optionally filtered by category.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List([FromQuery] ProductListRequest request, CancellationToken cancellationToken)
        => Ok(await productService.ListAsync(request, cancellationToken));

    /// <summary>Gets a single product by id. Reads are cached briefly (30s).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductResponse>> GetById(string id, CancellationToken cancellationToken)
        => Ok(await productService.GetByIdAsync(id, cancellationToken));

    /// <summary>Creates a product and its backing inventory record. Admin only.</summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var response = await productService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.ProductId }, response);
    }

    /// <summary>Updates a product's catalog fields. Admin only.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProductResponse>> Update(string id, UpdateProductRequest request, CancellationToken cancellationToken)
        => Ok(await productService.UpdateAsync(id, request, cancellationToken));

    /// <summary>Soft-deletes a product — historical orders keep working via their snapshotted item data. Admin only.</summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await productService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
