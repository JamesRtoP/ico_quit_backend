using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace MedApp.Api.Fhir;

internal static class FhirServiceCollectionExtensions
{
    public static IServiceCollection AddFhirGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FhirOptions>()
            .Bind(configuration.GetSection(FhirOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var useManagedIdentity = configuration.GetValue<bool>($"{FhirOptions.SectionName}:{nameof(FhirOptions.UseManagedIdentity)}");

        var client = services.AddHttpClient<IFhirGateway, FirelyFhirGateway>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<FhirOptions>>().Value;
            http.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
            // HAPI caches search results by default; a dose recorded a moment ago must be visible immediately.
            http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        });

        if (useManagedIdentity)
        {
            services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
            services.AddTransient<ManagedIdentityTokenHandler>();
            client.AddHttpMessageHandler<ManagedIdentityTokenHandler>();
        }

        return services;
    }
}
