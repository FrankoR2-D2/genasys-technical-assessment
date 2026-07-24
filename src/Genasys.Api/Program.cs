using Genasys.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder
    .ConfigureControllers()
    .ConfigureSwagger()
    .ConfigureDatabase()
    .ConfigureCaching()
    .ConfigureExceptionHandling()
    .ConfigureApplicationServices()
    .ConfigureHttpClients()
    .ConfigureAuthentication()
    .ConfigureAuthorization();

var app = builder.Build();

await app.WithSeededDatabaseAsync();

app
    .WithErrorHandling()
    .WithSwaggerDocs()
    .WithSecurityPipeline()
    .WithEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration test project.
public partial class Program;
