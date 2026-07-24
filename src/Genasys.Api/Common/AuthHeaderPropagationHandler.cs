namespace Genasys.Api.Common;

// The Order -> Inventory/Payment calls are real HTTP hops (loopback, but
// real), so they land back in this app's own [Authorize] pipeline. This
// forwards the original caller's bearer token onto those outbound requests
// instead of inventing a separate service-account identity.
public class AuthHeaderPropagationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
