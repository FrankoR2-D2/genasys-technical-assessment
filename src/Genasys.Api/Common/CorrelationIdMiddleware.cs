namespace Genasys.Api.Common;

// Accepts a caller-supplied X-Correlation-Id (or mints one), echoes it on
// the response, and wraps the rest of the pipeline in a logging scope so
// every log line for this request — across every service it touches —
// carries the same id. AuthHeaderPropagationHandler forwards it onto the
// Order -> Inventory/Payment HTTP hop so a single order creation is
// traceable end to end even though it crosses three controllers.
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
