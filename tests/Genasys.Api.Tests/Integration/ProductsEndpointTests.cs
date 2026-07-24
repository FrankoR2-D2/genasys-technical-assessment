using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Products;
using Xunit;

namespace Genasys.Api.Tests.Integration;

public class ProductsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client, "admin", "Admin123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Create_MissingName_Returns400WithValidationProblem()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            productId = $"BAD-{Guid.NewGuid():N}",
            name = "",
            sku = "BAD-1",
            unitPrice = 5m,
            initialQuantity = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Name");
    }

    [Fact]
    public async Task List_ReturnsPagedEnvelope()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync("/api/products?page=1&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(2);
        body.Items.Count.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task CreateGetUpdateDelete_FullLifecycle()
    {
        var client = await AuthenticatedClientAsync();
        var productId = $"LIFE-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync("/api/products", new
        {
            productId,
            name = "Lifecycle Widget",
            sku = productId,
            unitPrice = 12.5m,
            initialQuantity = 3
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResponse = await client.GetAsync($"/api/products/{productId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResponse = await client.PutAsJsonAsync($"/api/products/{productId}", new { name = "Updated Widget", unitPrice = 15m });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResponse = await client.DeleteAsync($"/api/products/{productId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDelete = await client.GetAsync($"/api/products/{productId}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
