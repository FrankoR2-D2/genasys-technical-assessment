using Genasys.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder
    .ConfigureControllers()
    .ConfigureSwagger()
    .ConfigureDatabase()
    .ConfigureCaching()
    .ConfigureExceptionHandling()
    .ConfigureHealthChecks()
    .ConfigureLogging()
    .ConfigureApplicationServices()
    .ConfigureHttpClients()
    .ConfigureAuthentication()
    .ConfigureAuthorization();

var app = builder.Build();

await app.WithSeededDatabaseAsync();

app
    .WithErrorHandling()
    .WithCorrelationId()
    .WithSwaggerDocs()
    .WithSecurityPipeline()
    .WithEndpoints()
    .WithHealthChecks();

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration test project.
public partial class Program;
