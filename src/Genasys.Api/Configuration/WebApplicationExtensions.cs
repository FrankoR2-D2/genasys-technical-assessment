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
