using Genasys.Api.Contracts.Auth;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Genasys.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Token(TokenRequest request, CancellationToken cancellationToken)
    {
        var token = await authService.AuthenticateAsync(request.Username, request.Password, cancellationToken);
        if (token is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "InvalidCredentials",
                Detail = "Username or password is incorrect."
            });
        }

        return Ok(token);
    }
}
