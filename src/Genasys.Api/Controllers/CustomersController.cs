using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Customers;
using Genasys.Api.Entities;
using Genasys.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController(ICustomerService customerService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerResponse>>> List([FromQuery] CustomerListRequest request, CancellationToken cancellationToken)
        => Ok(await customerService.ListAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await customerService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CustomerResponse>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var response = await customerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CustomerResponse>> Update(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
        => Ok(await customerService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await customerService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
