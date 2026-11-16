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

    private readonly FhirClient _client;
    private readonly int _maxResults;

    public FirelyFhirGateway(HttpClient httpClient, IOptions<FhirOptions> options)
    {
        _maxResults = options.Value.MaxSearchResults;
        _client = new FhirClient(options.Value.Url, httpClient, new FhirClientSettings
        {
            PreferredFormat = ResourceFormat.Json,
            VerifyFhirVersion = false,
            ReturnPreference = ReturnPreference.Representation,
        });
    }

    public async Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct)
    {
        var query = new SearchParams()
            .Add("identifier", $"{system}|{value}")
            .Add("_elements", "id")
            .LimitTo(2);

        var bundle = await _client.SearchAsync<Patient>(query, ct).ConfigureAwait(false);
        return Entries<Patient>(bundle).Select(p => p.Id).OfType<string>().Where(id => id.Length > 0).ToList();
    }

    public Task<IReadOnlyList<MedicationRequest>> SearchActiveMedicationRequestsAsync(string patientId, CancellationToken ct)
    {
        var query = new SearchParams()
            .Add("subject", PatientReference(patientId))
            .Add("status", "active")
            .LimitTo(PageSize);

        return SearchAllAsync<MedicationRequest>(query, ct);
    }

    public async Task<MedicationRequest?> FindMedicationRequestAsync(string patientId, string medicationRequestId, CancellationToken ct)
    {
        // Search (not read-by-id) so the subject constraint is enforced by the FHIR server itself.
        var query = new SearchParams()
            .Add("_id", medicationRequestId)
            .Add("subject", PatientReference(patientId))
            .LimitTo(2);

        var bundle = await _client.SearchAsync<MedicationRequest>(query, ct).ConfigureAwait(false);
        var matches = Entries<MedicationRequest>(bundle).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public Task<IReadOnlyList<MedicationAdministration>> SearchMedicationAdministrationsAsync(
        string patientId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        var query = new SearchParams()
            .Add("subject", PatientReference(patientId))
            .Add("effective-time", "ge" + FhirInstant(rangeStart))
            .Add("effective-time", "le" + FhirInstant(rangeEnd))
            .Add("_sort", "-effective-time")
            .LimitTo(PageSize);

        return SearchAllAsync<MedicationAdministration>(query, ct);
    }

    public async Task<MedicationAdministration> CreateMedicationAdministrationAsync(MedicationAdministration resource, CancellationToken ct)
    {
        var created = await _client.CreateAsync(resource, ct).ConfigureAwait(false);
        return created ?? throw new InvalidOperationException("FHIR server returned no MedicationAdministration on create.");
    }

    private async Task<IReadOnlyList<T>> SearchAllAsync<T>(SearchParams query, CancellationToken ct) where T : Resource, new()
    {
        var results = new List<T>();
        var bundle = await _client.SearchAsync<T>(query, ct).ConfigureAwait(false);

        while (bundle is not null)
        {
            results.AddRange(Entries<T>(bundle));
            if (results.Count >= _maxResults)
            {
                return results.Take(_maxResults).ToList();
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
