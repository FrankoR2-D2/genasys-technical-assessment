using Genasys.Api.Contracts.Common;
using Genasys.Api.Entities;

namespace Genasys.Api.Common;

public static class AddressMapper
{
    public static Address? ToEntity(AddressRequest? request) => request is null ? null : new Address
    {
        Line1 = request.Line1,
        Line2 = request.Line2,
        City = request.City,
        State = request.State,
        PostalCode = request.PostalCode,
        Country = request.Country
    };

    public static AddressResponse? ToResponse(Address? address) => address is null ? null : new AddressResponse(
        address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country);

    // EF Core owned types are identified by (owner, navigation) — the same
    // Address instance can never be assigned to two owners' navigations, so
    // reusing e.g. a Customer's address on an Order requires a real copy.
    public static Address? Clone(Address? address) => address is null ? null : new Address
    {
        Line1 = address.Line1,
        Line2 = address.Line2,
        City = address.City,
        State = address.State,
        PostalCode = address.PostalCode,
        Country = address.Country
    };
}
