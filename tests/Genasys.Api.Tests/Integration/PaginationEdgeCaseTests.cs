using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Products;
using Xunit;

namespace Genasys.Api.Tests.Integration;

public class PaginationEdgeCaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client, "admin", "Admin123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task List_PageBeyondLastPage_ReturnsEmptyItemsNotError()
    {
        var client = await AuthenticatedClientAsync();

        // DataSeeder always seeds a handful of products, so a page far
        // beyond any plausible total is guaranteed to be past the end.
        var response = await client.GetAsync("/api/products?page=9999&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().BeGreaterThan(0, "the seeded catalog is non-empty");
    }

    [Fact]
    public async Task List_UnrecognizedSortField_FallsBackToDefaultInsteadOfErroring()
    {
        var client = await AuthenticatedClientAsync();

        // "definitelyNotARealColumn" isn't in ProductService's sort
        // allow-list switch — it should fall through to the default case
        // (sort by name), not attempt to use it as a raw column and blow up.
        var response = await client.GetAsync("/api/products?sort=definitelyNotARealColumn:asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        body!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task List_SkipTakeTakesPrecedenceOverPageAndPageSize()
    {
        var client = await AuthenticatedClientAsync();
        var searchTerm = $"ZZPageTest-{Guid.NewGuid():N}";

        // Three products sharing a unique, alphabetically-ordered search
        // term so the default name-ascending sort gives a known sequence.
        foreach (var suffix in new[] { "A", "B", "C" })
        {
            var productId = $"PG-{Guid.NewGuid():N}";
            var createResponse = await client.PostAsJsonAsync("/api/products", new
            {
                productId,
                name = $"{searchTerm}-{suffix}",
                sku = productId,
                unitPrice = 1m,
                initialQuantity = 1
            });
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var pagedResponse = await client.GetAsync($"/api/products?search={searchTerm}&page=1&pageSize=1");
        var paged = await pagedResponse.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        paged!.Items.Should().ContainSingle().Which.Name.Should().EndWith("-A");

        // skip=1&take=1 must win over the page/pageSize above (both are
        // still present as defaults) and land on the second item instead.
        var skipTakeResponse = await client.GetAsync($"/api/products?search={searchTerm}&page=1&pageSize=1&skip=1&take=1");
        var skipTaken = await skipTakeResponse.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        skipTaken!.Items.Should().ContainSingle().Which.Name.Should().EndWith("-B");

        // The envelope still reports page/pageSize (computed back from
        // skip/take) so a client only ever has to understand one shape.
        skipTaken.PageSize.Should().Be(1);
        skipTaken.Page.Should().Be(2);
    }
}
