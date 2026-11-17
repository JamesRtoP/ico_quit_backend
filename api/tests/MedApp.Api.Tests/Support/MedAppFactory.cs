using System.Net.Http.Headers;
using MedApp.Api.Fhir;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MedApp.Api.Tests.Support;

/// <summary>
/// Hosts the real API in-process ("Testing" environment, LocalJwt auth, all logs captured at Trace).
/// FHIR is normally replaced by an in-memory <see cref="IFhirGateway"/>; <paramref name="fhirUrl"/> only feeds config validation.
/// </summary>
public sealed class MedAppFactory(
    string fhirUrl = "http://fhir.invalid/fhir",
    IFhirGateway? gateway = null,
    string environment = "Testing",
    IReadOnlyDictionary<string, string>? extraSettings = null)
    : WebApplicationFactory<Program>
{
    public TestTokens Tokens { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);

        var settings = new Dictionary<string, string>
        {
            ["Auth:Mode"] = "LocalJwt",
            ["Fhir:Url"] = fhirUrl,
            ["Fhir:UseManagedIdentity"] = "false",
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            ["Logging:LogLevel:System"] = "Trace",
        };
        foreach (var (k, v) in Tokens.Settings)
        {
            settings[k] = v;
        }
        foreach (var (k, v) in extraSettings ?? new Dictionary<string, string>())
        {
            settings[k] = v;
        }
        foreach (var (k, v) in settings)
        {
            builder.UseSetting(k, v);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });

        if (gateway is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFhirGateway>();
                services.AddSingleton(gateway);
            });
        }
    }

    public HttpClient Client(string? bearerToken = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        if (bearerToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return client;
    }

    public HttpClient ClientFor(string? oid) => Client(Tokens.For(oid));
}
