using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Genasys.Api.Common;

// Single catch-all: domain exceptions map to their own status code, EF Core
// concurrency conflicts map to 409, everything else is an opaque 500 so
// internals never leak into a response body.
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            DomainException domainException => (domainException.StatusCode, domainException.GetType().Name, domainException.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "ConcurrencyConflict", "The record was modified by another request. Reload and try again."),
            _ => (StatusCodes.Status500InternalServerError, "UnexpectedError", "An unexpected error occurred.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }
        else
        {
            logger.LogWarning(exception, "Handled exception: {Title}", title);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        }, cancellationToken);

        return true;
    }
}
