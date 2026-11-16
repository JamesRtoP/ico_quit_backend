using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MedApp.Api.Infrastructure;

/// <summary>OpenAPI document (dev/test only) — the contract for the mobile and chatbot teams.</summary>
internal static class OpenApiSetup
{
    public const string DocumentPath = "/openapi/v1.json";

    public static IServiceCollection AddMedAppOpenApi(this IServiceCollection services) =>
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "MedApp API",
                    Version = "v1",
                    Description = "Medication adherence API. All /me endpoints act on the authenticated caller only. Synthetic data only outside production.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Microsoft Entra External ID access token (locally: dotnet user-jwts).",
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] },
                ];
                return Task.CompletedTask;
            });
        });
}
