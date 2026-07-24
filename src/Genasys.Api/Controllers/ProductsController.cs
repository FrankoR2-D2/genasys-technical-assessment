using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Products;
using Genasys.Api.Entities;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List([FromQuery] ProductListRequest request, CancellationToken cancellationToken)
        => Ok(await productService.ListAsync(request, cancellationToken));

    [HttpGet("{id}")]
    public async Task<ActionResult<ProductResponse>> GetById(string id, CancellationToken cancellationToken)
        => Ok(await productService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var response = await productService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.ProductId }, response);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProductResponse>> Update(string id, UpdateProductRequest request, CancellationToken cancellationToken)
        => Ok(await productService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await productService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
