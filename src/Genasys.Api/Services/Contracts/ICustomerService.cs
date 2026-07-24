using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Customers;

namespace Genasys.Api.Services.Contracts;

public interface ICustomerService
{
    Task<PagedResult<CustomerResponse>> ListAsync(CustomerListRequest request, CancellationToken cancellationToken);
    Task<CustomerResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
