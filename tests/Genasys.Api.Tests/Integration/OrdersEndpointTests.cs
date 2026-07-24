using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Genasys.Api.Contracts.Customers;
using Genasys.Api.Contracts.Inventory;
using Genasys.Api.Contracts.Orders;
using Genasys.Api.Contracts.Products;
using Xunit;

namespace Genasys.Api.Tests.Integration;

public class OrdersEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var token = await factory.GetTokenAsync(client, "admin", "Admin123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Order Test Customer",
            email = $"{Guid.NewGuid():N}@example.com"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerResponse>();
        return body!.Id;
    }

    private static async Task<string> CreateProductAsync(HttpClient client, int initialQuantity)
    {
        var productId = $"ORD-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            productId,
            name = "Order Test Widget",
            sku = productId,
            unitPrice = 10m,
            initialQuantity
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ProductResponse>();
        return body!.ProductId;
    }

    [Fact]
    public async Task Create_HappyPath_Returns201ConfirmedAndDebitsInventory()
    {
        var client = await AuthenticatedClientAsync();
        var customerId = await CreateCustomerAsync(client);
        var productId = await CreateProductAsync(client, initialQuantity: 5);

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            CustomerId = customerId,
            Items = [new CreateOrderItemRequest { ProductId = productId, Quantity = 2 }]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        order!.Status.Should().Be("Confirmed");
        order.TotalAmount.Should().Be(20m);

        var inventoryResponse = await client.GetAsync($"/api/inventory/{productId}");
        var inventory = await inventoryResponse.Content.ReadFromJsonAsync<InventoryItemResponse>();
        inventory!.AvailableQuantity.Should().Be(3);
        inventory.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task Create_InsufficientInventory_Returns409AndReservesNothing()
    {
        var client = await AuthenticatedClientAsync();
        var customerId = await CreateCustomerAsync(client);
        var productId = await CreateProductAsync(client, initialQuantity: 1);

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            CustomerId = customerId,
            Items = [new CreateOrderItemRequest { ProductId = productId, Quantity = 5 }]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var inventoryResponse = await client.GetAsync($"/api/inventory/{productId}");
        var inventory = await inventoryResponse.Content.ReadFromJsonAsync<InventoryItemResponse>();
        inventory!.AvailableQuantity.Should().Be(1);
    }

    [Fact]
    public async Task Create_PaymentDeclined_Returns402AndReleasesInventory()
    {
        var client = await AuthenticatedClientAsync();
        var customerId = await CreateCustomerAsync(client);
        var productId = await CreateProductAsync(client, initialQuantity: 5);

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            CustomerId = customerId,
            Items = [new CreateOrderItemRequest { ProductId = productId, Quantity = 2 }],
            PaymentInstrumentReference = "DECLINE"
        });

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        var inventoryResponse = await client.GetAsync($"/api/inventory/{productId}");
        var inventory = await inventoryResponse.Content.ReadFromJsonAsync<InventoryItemResponse>();
        inventory!.AvailableQuantity.Should().Be(5);
        inventory.ReservedQuantity.Should().Be(0);
    }
}
