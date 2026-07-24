using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using Genasys.Api.Clients;
using Genasys.Api.Common;
using Genasys.Api.Data;
using Genasys.Api.Filters;
using Genasys.Api.Services;
using Genasys.Api.Services.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Polly;

namespace Genasys.Api.Configuration;

// One method per concern, chained in Program.cs, so the composition root
// reads as a table of contents instead of fifty lines of builder.Services.Add*.
public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder ConfigureControllers(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddScoped<ValidationFilter>();
        builder.Services.AddValidatorsFromAssemblyContaining<Program>();

        return builder;
    }

    public static WebApplicationBuilder ConfigureSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Genasys Order Processing API",
                Version = "v1",
                Description = "Order, Inventory, and Payment endpoints for the Genasys technical assessment. " +
                              "Call POST /api/auth/token first (admin/Admin123! or viewer/Viewer123!), then Authorize with the returned token."
            });

            var bearerScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the accessToken returned by POST /api/auth/token.",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            options.AddSecurityDefinition("Bearer", bearerScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { { bearerScheme, [] } });
        });

        return builder;
    }

    public static WebApplicationBuilder ConfigureDatabase(this WebApplicationBuilder builder)
    {
        var inMemoryDbName = builder.Configuration["Database:Name"] ?? "GenasysOrderProcessing";
        builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(inMemoryDbName));

        return builder;
    }

    public static WebApplicationBuilder ConfigureCaching(this WebApplicationBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<KeyedLockProvider>();

        return builder;
    }

    public static WebApplicationBuilder ConfigureExceptionHandling(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        return builder;
    }

    public static WebApplicationBuilder ConfigureApplicationServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IProductService, ProductService>();
        builder.Services.AddScoped<ICustomerService, CustomerService>();
        builder.Services.AddScoped<IInventoryService, InventoryService>();
        builder.Services.AddScoped<IPaymentService, PaymentService>();
        builder.Services.AddScoped<IOrderService, OrderService>();

        return builder;
    }

    public static WebApplicationBuilder ConfigureHttpClients(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<AuthHeaderPropagationHandler>();

        // Order -> Inventory/Payment go over real HTTP (per the spec's
        // inter-service HTTP client requirement) even though everything
        // lives in one process — SelfBaseUrl points back at this app's own
        // listen address.
        var selfBaseUrl = builder.Configuration["Services:SelfBaseUrl"] ?? "http://localhost:5148";

        builder.Services.AddHttpClient<IInventoryApiClient, InventoryApiClient>(client =>
            {
                client.BaseAddress = new Uri(selfBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<AuthHeaderPropagationHandler>()
            .AddTransientHttpErrorPolicy(policy => policy.WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt)));

        builder.Services.AddHttpClient<IPaymentApiClient, PaymentApiClient>(client =>
            {
                client.BaseAddress = new Uri(selfBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<AuthHeaderPropagationHandler>()
            .AddTransientHttpErrorPolicy(policy => policy.WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt)));

        return builder;
    }

    public static WebApplicationBuilder ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

        var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSection["Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        return builder;
    }

    public static WebApplicationBuilder ConfigureAuthorization(this WebApplicationBuilder builder)
    {
        // [AllowAnonymous] on /api/auth/token is the only opt-out — everything else requires a bearer token by default.
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return builder;
    }
}
