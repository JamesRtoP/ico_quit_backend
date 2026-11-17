using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Options;

namespace MedApp.Api.Fhir;

/// <summary>
/// <see cref="IFhirGateway"/> over the Firely SDK <see cref="FhirClient"/>. The HttpClient comes from
/// IHttpClientFactory, so the managed-identity handler can be added via config without touching this class.
/// </summary>
internal sealed class FirelyFhirGateway : IFhirGateway, IDisposable
{
    private readonly FhirClient _client;

    public FirelyFhirGateway(HttpClient httpClient, IOptions<FhirOptions> options)
    {
        _client = new FhirClient(options.Value.Url, httpClient, new FhirClientSettings
        {
            PreferredFormat = ResourceFormat.Json,
            VerifyFhirVersion = false,
        });
    }

    public async Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct)
    {
        // Ask for ids only, so names and birth dates never reach the API; 2 is enough to detect "more than one".
        var query = new SearchParams()
            .Add("identifier", $"{system}|{value}")
            .Add("_elements", "id")
            .LimitTo(2);

        var bundle = await _client.SearchAsync<Patient>(query, ct).ConfigureAwait(false);
        return Entries<Patient>(bundle).Select(p => p.Id).OfType<string>().Where(id => id.Length > 0).ToList();
    }

    // Only "match" entries of the requested type; ignores _include results and OperationOutcomes.
    private static IEnumerable<T> Entries<T>(Bundle? bundle) where T : Resource =>
        bundle?.Entry
            .Where(e => e.Search?.Mode is null or Bundle.SearchEntryMode.Match)
            .Select(e => e.Resource)
            .OfType<T>()
        ?? [];

    public void Dispose() => _client.Dispose();
}
