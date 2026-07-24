using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Contracts.Payments;
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

    // Inventory reserve/release and payment processing are deliberately
    // *not* Admin-gated — OrderService calls them on behalf of whoever is
    // placing an order, so they only require an authenticated caller, same
    // as order creation itself. See auth-and-security.md for the tradeoff:
    // this also means a non-admin can call them directly, not only through
    // OrderService. These tests document that as the intended behavior
    // rather than leaving it as an implicit, unverified assumption.
    [Fact]
    public async Task NonAdminAuthenticatedUser_CanReserveAndReleaseInventoryDirectly()
    {
        var adminClient = factory.CreateClient();
        var adminToken = await factory.GetTokenAsync(adminClient, "admin", "Admin123!");
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var productId = $"AUTHZ-{Guid.NewGuid():N}";
        var createResponse = await adminClient.PostAsJsonAsync("/api/products", new
        {
            productId,
            name = "Authz Test Widget",
            sku = productId,
            unitPrice = 5m,
            initialQuantity = 5
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var viewerClient = factory.CreateClient();
        var viewerToken = await factory.GetTokenAsync(viewerClient, "viewer", "Viewer123!");
        viewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);

        var orderId = Guid.NewGuid();
        var reserveResponse = await viewerClient.PostAsJsonAsync($"/api/inventory/{productId}/reserve",
            new ReserveInventoryRequest { OrderId = orderId, Quantity = 1 });
        reserveResponse.StatusCode.Should().Be(HttpStatusCode.OK, "reserve is operational, not administrative");

        var releaseResponse = await viewerClient.PostAsJsonAsync($"/api/inventory/{productId}/release",
            new ReleaseInventoryRequest { OrderId = orderId, Quantity = 1 });
        releaseResponse.StatusCode.Should().Be(HttpStatusCode.OK, "release is operational, not administrative");
    }

    [Fact]
    public async Task NonAdminAuthenticatedUser_CanProcessPaymentDirectly()
    {
        var viewerClient = factory.CreateClient();
        var viewerToken = await factory.GetTokenAsync(viewerClient, "viewer", "Viewer123!");
        viewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);

        var response = await viewerClient.PostAsJsonAsync("/api/payments/process", new ProcessPaymentRequest
        {
            OrderId = Guid.NewGuid(),
            Amount = 10m
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "payment processing is operational, not administrative");
    }
}
