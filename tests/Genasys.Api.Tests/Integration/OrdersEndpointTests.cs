using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Genasys.Api.Contracts.Common;
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

    [Fact]
    public async Task Create_ConcurrentDuplicateIdempotencyKey_CreatesExactlyOneOrder()
    {
        var client = await AuthenticatedClientAsync();
        var customerId = await CreateCustomerAsync(client);
        var productId = await CreateProductAsync(client, initialQuantity: 5);
        var idempotencyKey = Guid.NewGuid().ToString();

        async Task<HttpResponseMessage> Send()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new CreateOrderRequest
                {
                    CustomerId = customerId,
                    Items = [new CreateOrderItemRequest { ProductId = productId, Quantity = 1 }]
                })
            };
            message.Headers.Add("Idempotency-Key", idempotencyKey);
            return await client.SendAsync(message);
        }

        // Two requests racing on the same key — the unique IdempotencyKey
        // index means only one can win the insert; the loser must recover
        // cleanly (see OrderService.CreateAsync's DbUpdateException catch)
        // rather than surface as an unhandled 500.
        var responses = await Task.WhenAll(Send(), Send());

        foreach (var response in responses)
        {
            ((int)response.StatusCode).Should().BeLessThan(500, "a lost idempotency race must not surface as an unhandled error");
        }

        var orderIds = new HashSet<Guid>();
        foreach (var response in responses)
        {
            var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
            orderIds.Add(order!.Id);
        }
        orderIds.Should().HaveCount(1, "both requests carried the same idempotency key and must resolve to the same order");

        var listResponse = await client.GetAsync($"/api/orders?customerId={customerId}");
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<OrderResponse>>();
        list!.Items.Should().HaveCount(1);

        var inventoryResponse = await client.GetAsync($"/api/inventory/{productId}");
        var inventory = await inventoryResponse.Content.ReadFromJsonAsync<InventoryItemResponse>();
        inventory!.AvailableQuantity.Should().Be(4, "only the winning order's single-unit reservation should survive");
    }

    [Fact]
    public async Task SoftDeletedProductAndCustomer_StillResolveViaHistoricalOrderSnapshot()
    {
        var client = await AuthenticatedClientAsync();
        var customerId = await CreateCustomerAsync(client);
        var productId = await CreateProductAsync(client, initialQuantity: 5);

        var orderResponse = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            CustomerId = customerId,
            Items = [new CreateOrderItemRequest { ProductId = productId, Quantity = 1 }]
        });
        orderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();

        (await client.DeleteAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/customers/{customerId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Soft-deleted resources disappear from direct lookup...
        (await client.GetAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/customers/{customerId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ...and from list/search results.
        var listResponse = await client.GetAsync($"/api/products?search={Uri.EscapeDataString(productId)}");
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        list!.Items.Should().NotContain(p => p.ProductId == productId);

        // ...but the historical order still resolves fully via its snapshot,
        // with nothing to cascade or null out.
        var historicalResponse = await client.GetAsync($"/api/orders/{order!.Id}");
        historicalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var historicalOrder = await historicalResponse.Content.ReadFromJsonAsync<OrderResponse>();
        historicalOrder!.CustomerName.Should().NotBeNullOrWhiteSpace();
        historicalOrder.Items.Single().ProductName.Should().NotBeNullOrWhiteSpace();
        historicalOrder.Status.Should().Be("Confirmed");
    }
}
