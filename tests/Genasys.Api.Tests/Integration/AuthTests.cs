using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Genasys.Api.Contracts.Auth;
using Xunit;

namespace Genasys.Api.Tests.Integration;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Token_ValidCredentials_ReturnsAccessToken()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { username = "admin", password = "Admin123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Token_InvalidCredentials_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token", new { username = "admin", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
