namespace Genasys.Api.Common;

// The Order -> Inventory/Payment calls are real HTTP hops (loopback, but
// real), so they land back in this app's own [Authorize] pipeline. This
// forwards the original caller's bearer token onto those outbound requests
// instead of inventing a separate service-account identity, and forwards
// the correlation id (CorrelationIdMiddleware) so the same request is
// traceable across all three controllers it touches.
public class AuthHeaderPropagationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var authHeader = httpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        }

        if (httpContext?.Items[CorrelationIdMiddleware.HeaderName] is string correlationId)
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
