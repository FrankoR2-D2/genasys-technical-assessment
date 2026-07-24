namespace Genasys.Api.Contracts.Auth;

public class TokenRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public record TokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds);
