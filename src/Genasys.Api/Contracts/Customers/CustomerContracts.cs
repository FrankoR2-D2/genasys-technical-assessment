using Genasys.Api.Contracts.Common;

namespace Genasys.Api.Contracts.Customers;

public class CreateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public AddressRequest? ShippingAddress { get; set; }
    public AddressRequest? BillingAddress { get; set; }
}

public class UpdateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public AddressRequest? ShippingAddress { get; set; }
    public AddressRequest? BillingAddress { get; set; }
}

public class CustomerListRequest : PagedRequest;

public record CustomerResponse(
    Guid Id,
    string Name,
    string Email,
    AddressResponse? ShippingAddress,
    AddressResponse? BillingAddress,
    DateTime CreatedAt,
    DateTime UpdatedAt);
