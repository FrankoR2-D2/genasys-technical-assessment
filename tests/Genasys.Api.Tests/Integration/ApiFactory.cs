using System.Net.Http.Json;
using Genasys.Api.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Genasys.Api.Tests.Integration;

// A fresh InMemory database name per factory instance keeps test classes
// (each an IClassFixture<ApiFactory>) from tripping over each other's data.
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Name", Guid.NewGuid().ToString());
    }

    public async Task<string> GetTokenAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.AccessToken;
    }
}
