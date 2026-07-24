using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Genasys.Api.Common;
using Genasys.Api.Contracts.Auth;
using Genasys.Api.Data;
using Genasys.Api.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Genasys.Api.Services;

public class AuthService(AppDbContext db, IOptions<JwtOptions> jwtOptions, ILogger<AuthService> logger) : IAuthService
{
    public async Task<TokenResponse?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            logger.LogWarning("Failed login attempt for username {Username}", username);
            return null;
        }

        var options = jwtOptions.Value;
        var expires = DateTime.UtcNow.AddMinutes(options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResponse(accessToken, "Bearer", options.ExpiryMinutes * 60);
    }
}
