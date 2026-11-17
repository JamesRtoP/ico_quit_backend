using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace MedApp.Api.Auth;

/// <summary>
/// JWT bearer authentication.
/// <list type="bullet">
/// <item><c>Auth:Mode = ExternalId</c> (default): Microsoft Entra External ID via Microsoft.Identity.Web, config section <c>ExternalId</c>.</item>
/// <item><c>Auth:Mode = LocalJwt</c>: tokens from <c>dotnet user-jwts</c>; issuer, audiences and signing key are read from
/// <c>Authentication:Schemes:Bearer</c> (user-jwts writes them to appsettings.Development.json and user secrets).
/// Only allowed in the Development and Testing environments.</item>
/// </list>
/// </summary>
internal static class AuthenticationSetup
{
    public const string ModeExternalId = "ExternalId";
    public const string ModeLocalJwt = "LocalJwt";
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// Authorization policy for <c>/chatbot/*</c>: a delegated (on-behalf-of) token that was issued to the chatbot's
    /// client application. The patient still comes from the token's <c>oid</c> (via CallerPatientFilter).
    /// </summary>
    public const string ChatbotPolicy = "Chatbot";

    /// <summary>Config key holding the chatbot app registration's client id. Empty or missing denies every chatbot call.</summary>
    public const string ChatbotClientIdKey = "Chatbot:ClientId";

    public static IServiceCollection AddMedAppAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var mode = configuration["Auth:Mode"] ?? ModeExternalId;

        if (string.Equals(mode, ModeLocalJwt, StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment(TestingEnvironment))
            {
                throw new InvalidOperationException(
                    $"Auth:Mode '{ModeLocalJwt}' is only allowed in Development or {TestingEnvironment}.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // Keep claim names as issued ("oid", not the long WS-Fed URI).
                    options.MapInboundClaims = false;
                });
        }
        else if (string.Equals(mode, ModeExternalId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(configuration["ExternalId:ClientId"]))
            {
                throw new InvalidOperationException(
                    "ExternalId:ClientId is not configured. Configure Entra External ID, or use Auth:Mode=LocalJwt in Development.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(configuration, configSectionName: "ExternalId");
        }
        else
        {
            throw new InvalidOperationException($"Unknown Auth:Mode '{mode}'.");
        }

        // Rule 5: every endpoint requires an authenticated user unless it explicitly opts out.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(ChatbotPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.WasIssuedToClient(configuration[ChatbotClientIdKey])));

        return services;
    }

    /// <summary>
    /// True when the token was issued to <paramref name="clientId"/>: the client application that requested it, in
    /// <c>azp</c> (v2 tokens) or <c>appid</c> (v1 tokens). In an on-behalf-of token this is the chatbot, while
    /// <c>oid</c> is still the patient. False when <paramref name="clientId"/> is not configured (fail closed).
    /// </summary>
    public static bool WasIssuedToClient(this ClaimsPrincipal user, string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }
        var issuedTo = user.FindFirstValue("azp") ?? user.FindFirstValue("appid");
        return string.Equals(issuedTo, clientId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the caller's Entra object id from the validated token, or null when absent or not a GUID.
    /// Accepts both the short ("oid") and the mapped long claim type.
    /// </summary>
    public static string? GetCallerObjectId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimConstants.Oid) ?? user.FindFirstValue(ClaimConstants.ObjectId);
        return Guid.TryParse(value, out var oid) ? oid.ToString("D") : null;
    }
}
