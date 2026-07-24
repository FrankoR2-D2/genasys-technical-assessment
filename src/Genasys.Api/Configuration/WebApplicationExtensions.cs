using Genasys.Api.Common;
using Genasys.Api.Data;
using Genasys.Api.Data.Seed;

namespace Genasys.Api.Configuration;

public static class WebApplicationExtensions
{
    public static WebApplication WithErrorHandling(this WebApplication app)
    {
        app.UseExceptionHandler();
        return app;
    }

    // After the exception handler (so a failure in here is still caught)
    // and before everything else (so the correlation id's logging scope
    // covers auth/authz and the controller pipeline too).
    public static WebApplication WithCorrelationId(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        return app;
    }

    public static WebApplication WithSwaggerDocs(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        return app;
    }

    public static WebApplication WithSecurityPipeline(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static WebApplication WithEndpoints(this WebApplication app)
    {
        app.MapControllers();
        return app;
    }

    // Unauthenticated on purpose — a load balancer/orchestrator probing
    // liveness shouldn't need a bearer token, and this reports process
    // health only, not domain state.
    public static WebApplication WithHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health").AllowAnonymous();
        return app;
    }

    // Dummy data only — the InMemory database is empty on every process
    // start, so this runs unconditionally rather than gated to Development.
    public static async Task<WebApplication> WithSeededDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DataSeeder.SeedAsync(db);

        return app;
    }
}
