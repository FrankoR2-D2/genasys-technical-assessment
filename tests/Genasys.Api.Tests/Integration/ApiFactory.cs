using System.Net.Http.Json;
using Genasys.Api.Clients;
using Genasys.Api.Contracts.Auth;
using Genasys.Api.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Genasys.Api.Tests.Integration;

// A fresh InMemory database name per factory instance keeps test classes
// (each an IClassFixture<ApiFactory>) from tripping over each other's data.
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Name", Guid.NewGuid().ToString());

        // OrderService's typed HttpClients are configured with a real
        // loopback base address (Program.cs) — there's no socket listening
        // on it inside WebApplicationFactory's in-memory TestServer. Swap
        // them for the same in-process adapters the unit tests use, so a
        // request through this factory's HttpClient still exercises the
        // real OrdersController/ValidationFilter/GlobalExceptionHandler
        // pipeline end to end, just without a real network hop underneath.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInventoryApiClient>();
            services.AddScoped<IInventoryApiClient, InProcessInventoryApiClient>();

            services.RemoveAll<IPaymentApiClient>();
            services.AddScoped<IPaymentApiClient, InProcessPaymentApiClient>();
        });
    }

    public async Task<string> GetTokenAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.AccessToken;
    }
}
