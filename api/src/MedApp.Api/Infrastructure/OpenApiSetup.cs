using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MedApp.Api.Infrastructure;

/// <summary>
/// OpenAPI document: the contract for the mobile and chatbot teams (Foundry imports it as an OpenAPI tool).
/// Generated only in the Testing environment, where a test snapshots it to docs/openapi.json. Never served otherwise.
/// </summary>
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
                    Description = "MedApp API. /user endpoints act on the signed-in patient only (patient token). /chatbot endpoints act on the signed-in patient through the chatbot (delegated on-behalf-of token issued to the chatbot app). Synthetic data only outside production.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Microsoft Entra access token: the patient's own External ID token for /user; for /chatbot, the patient's token exchanged by the chatbot via the on-behalf-of flow (locally: dotnet user-jwts).",
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] },
                ];
                return Task.CompletedTask;
            });
        });
}
