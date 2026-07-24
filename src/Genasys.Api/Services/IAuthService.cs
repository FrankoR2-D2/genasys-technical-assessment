using Genasys.Api.Contracts.Auth;

namespace Genasys.Api.Services;

public interface IAuthService
{
    Task<TokenResponse?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
}
