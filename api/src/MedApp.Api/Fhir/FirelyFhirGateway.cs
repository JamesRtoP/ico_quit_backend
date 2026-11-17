using System.Globalization;
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
    private const int PageSize = 100;

    /// <summary>Upper bound on resources returned from one paged search, to cap work per request.</summary>
    internal const int MaxSearchResults = 1000;

    private readonly FhirClient _client;

    public FirelyFhirGateway(HttpClient httpClient, IOptions<FhirOptions> options)
    {
        _client = new FhirClient(options.Value.Url, httpClient, new FhirClientSettings
        {
            PreferredFormat = ResourceFormat.Json,
            VerifyFhirVersion = false,
            ReturnPreference = ReturnPreference.Representation,
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

    public Task<Observation?> FindObservationByIdentifierAsync(string patientId, string system, string value, CancellationToken ct) =>
        FindOwnedByIdentifierAsync<Observation>(patientId, system, value, ct);

    public Task<IReadOnlyList<Observation>> SearchObservationsAsync(
        string patientId, string codeSystem, string code, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        var query = new SearchParams()
            .Add("subject", PatientReference(patientId))
            .Add("code", $"{codeSystem}|{code}")
            .Add("date", "ge" + FhirInstant(rangeStart))
            .Add("date", "le" + FhirInstant(rangeEnd))
            .Add("_sort", "-date")
            .LimitTo(PageSize);

        return SearchAllAsync<Observation>(query, ct);
    }

    public Task<Observation> CreateObservationAsync(Observation resource, CancellationToken ct) => CreateAsync(resource, ct);

    public Task<Communication?> FindCommunicationByIdentifierAsync(string patientId, string system, string value, CancellationToken ct) =>
        FindOwnedByIdentifierAsync<Communication>(patientId, system, value, ct);

    public Task<Communication> CreateCommunicationAsync(Communication resource, CancellationToken ct) => CreateAsync(resource, ct);

    // Search (not read-by-id) with subject, so the FHIR server itself enforces ownership.
    private async Task<T?> FindOwnedByIdentifierAsync<T>(string patientId, string system, string value, CancellationToken ct)
        where T : Resource, new()
    {
        var query = new SearchParams()
            .Add("identifier", $"{system}|{value}")
            .Add("subject", PatientReference(patientId))
            .LimitTo(2);

        var bundle = await _client.SearchAsync<T>(query, ct).ConfigureAwait(false);
        return Entries<T>(bundle).FirstOrDefault();
    }

    private async Task<T> CreateAsync<T>(T resource, CancellationToken ct) where T : Resource
    {
        var created = await _client.CreateAsync(resource, ct).ConfigureAwait(false);
        return created ?? throw new InvalidOperationException($"FHIR server returned no {resource.TypeName} on create.");
    }

    private async Task<IReadOnlyList<T>> SearchAllAsync<T>(SearchParams query, CancellationToken ct) where T : Resource, new()
    {
        var results = new List<T>();
        var bundle = await _client.SearchAsync<T>(query, ct).ConfigureAwait(false);

        while (bundle is not null)
        {
            results.AddRange(Entries<T>(bundle));
            if (results.Count >= MaxSearchResults)
            {
                return results.Take(MaxSearchResults).ToList();
            }
            bundle = await _client.ContinueAsync(bundle, ct: ct).ConfigureAwait(false);
        }

        return results;
    }

    // Only "match" entries of the requested type; ignores _include results and OperationOutcomes.
    private static IEnumerable<T> Entries<T>(Bundle? bundle) where T : Resource =>
        bundle?.Entry
            .Where(e => e.Search?.Mode is null or Bundle.SearchEntryMode.Match)
            .Select(e => e.Resource)
            .OfType<T>()
        ?? [];

    public void Dispose() => _client.Dispose();

    private static string PatientReference(string patientId) => $"Patient/{patientId}";

    private static string FhirInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
