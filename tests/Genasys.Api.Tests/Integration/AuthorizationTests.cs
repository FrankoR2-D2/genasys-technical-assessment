using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Genasys.Api.Tests.Integration;

public class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ProtectedEndpoint_NoToken_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminOnlyEndpoint_NonAdminToken_Returns403()
    {
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client, "viewer", "Viewer123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            productId = $"TEST-{Guid.NewGuid():N}",
            name = "Test",
            sku = "TEST-1",
            unitPrice = 5m,
            initialQuantity = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminOnlyEndpoint_AdminToken_Returns201()
    {
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client, "admin", "Admin123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            productId = $"TEST-{Guid.NewGuid():N}",
            name = "Test",
            sku = "TEST-1",
            unitPrice = 5m,
            initialQuantity = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
