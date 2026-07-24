namespace Genasys.Api.Contracts.Common;

public class AddressRequest
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public record AddressResponse(
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country);
