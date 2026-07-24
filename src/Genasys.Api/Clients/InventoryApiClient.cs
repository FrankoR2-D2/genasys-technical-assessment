using System.Net;
using System.Net.Http.Json;
using Genasys.Api.Common;
using Genasys.Api.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Clients;

public class InventoryApiClient(HttpClient httpClient) : IInventoryApiClient
{
    public async Task<InventoryItemResponse> GetAsync(string productId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"api/inventory/{Uri.EscapeDataString(productId)}", cancellationToken);
        return await HandleAsync<InventoryItemResponse>(response, cancellationToken);
    }

    public async Task<InventoryItemResponse> ReserveAsync(string productId, ReserveInventoryRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync($"api/inventory/{Uri.EscapeDataString(productId)}/reserve", request, cancellationToken);
        return await HandleAsync<InventoryItemResponse>(response, cancellationToken);
    }

    public async Task<InventoryItemResponse> ReleaseAsync(string productId, ReleaseInventoryRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync($"api/inventory/{Uri.EscapeDataString(productId)}/release", request, cancellationToken);
        return await HandleAsync<InventoryItemResponse>(response, cancellationToken);
    }

    private static async Task<T> HandleAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);

        throw response.StatusCode switch
        {
            HttpStatusCode.Conflict => new InsufficientInventoryException(problem?.Detail ?? "Insufficient inventory."),
            HttpStatusCode.NotFound => new NotFoundException(problem?.Detail ?? "Inventory item not found."),
            HttpStatusCode.BadRequest => new ConflictException(problem?.Detail ?? "Invalid inventory request."),
            _ => new UpstreamServiceUnavailableException($"Inventory service returned {(int)response.StatusCode}.")
        };
    }

    private static async Task<ProblemDetails?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
