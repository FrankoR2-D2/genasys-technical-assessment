using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Customers;
using Genasys.Api.Entities;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

/// <summary>Customers an order is placed for — not part of the spec's required models, exposed as a full CRUD resource.</summary>
[ApiController]
[Route("api/customers")]
public class CustomersController(ICustomerService customerService) : ControllerBase
{
    /// <summary>Lists customers, paginated and searchable by name/email.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerResponse>>> List([FromQuery] CustomerListRequest request, CancellationToken cancellationToken)
        => Ok(await customerService.ListAsync(request, cancellationToken));

    /// <summary>Gets a single customer by id.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await customerService.GetByIdAsync(id, cancellationToken));

    /// <summary>Creates a customer. Email must be unique. Admin only.</summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CustomerResponse>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var response = await customerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    /// <summary>Updates a customer's profile and addresses. Admin only.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CustomerResponse>> Update(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
        => Ok(await customerService.UpdateAsync(id, request, cancellationToken));

    /// <summary>Soft-deletes a customer — historical orders keep working via their snapshotted name/address. Admin only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await customerService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
