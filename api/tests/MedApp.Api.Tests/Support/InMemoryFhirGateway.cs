using System.Collections.Concurrent;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MedApp.Api.Fhir;
using Task = System.Threading.Tasks.Task;

namespace MedApp.Api.Tests.Support;

/// <summary>
/// In-memory FHIR store loaded from the seed bundle. Mimics FHIR search semantics for the queries the API makes,
/// and records which patient each call was scoped to.
/// </summary>
public sealed class InMemoryFhirGateway : IFhirGateway
{
    private readonly ConcurrentDictionary<string, Resource> _store = new();

    public ConcurrentQueue<(string Operation, string? PatientId)> Calls { get; } = new();

    /// <summary>When set, every call throws this (to exercise error paths).</summary>
    public Func<Exception>? Fault { get; set; }

    public static InMemoryFhirGateway FromSeed()
    {
        var gateway = new InMemoryFhirGateway();
        var bundle = new FhirJsonDeserializer().Deserialize<Bundle>(File.ReadAllText(SeedData.BundlePath));
        foreach (var resource in bundle.Entry.Select(e => e.Resource).OfType<Resource>())
        {
            gateway._store[Key(resource)] = resource;
        }
        return gateway;
    }

    public IEnumerable<Observation> Observations => _store.Values.OfType<Observation>();

    public IEnumerable<Communication> Communications => _store.Values.OfType<Communication>();

    public Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct)
    {
        Record("Patient?identifier", null);
        IReadOnlyList<string> ids = _store.Values.OfType<Patient>()
            .Where(p => p.Identifier.Any(i => i.System == system && i.Value == value))
            .Select(p => p.Id!)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<Observation?> FindObservationByIdentifierAsync(string patientId, string system, string value, CancellationToken ct)
    {
        Record("Observation?identifier&subject", patientId);
        return Task.FromResult(FindOwned(Observations, o => o.Subject, o => o.Identifier, patientId, system, value));
    }

    public Task<IReadOnlyList<Observation>> SearchObservationsAsync(
        string patientId, string codeSystem, string code, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        Record("Observation?subject&code&date", patientId);
        IReadOnlyList<Observation> result = Observations
            .Where(o => o.Subject?.Reference == $"Patient/{patientId}")
            .Where(o => o.Code?.Coding.Any(c => c.System == codeSystem && c.Code == code) == true)
            .Select(o => (Obs: o, When: (o.Effective as FhirDateTime)?.ToDateTimeOffset(TimeSpan.Zero)))
            .Where(x => x.When is { } w && w >= rangeStart && w <= rangeEnd)
            .OrderByDescending(x => x.When)
            .Select(x => (Observation)x.Obs.DeepCopy())
            .ToList();
        return Task.FromResult(result);
    }

    public Task<Observation> CreateObservationAsync(Observation resource, CancellationToken ct)
    {
        Record("Observation create", PatientIdOf(resource.Subject));
        return Task.FromResult(Store(resource));
    }

    public Task<Communication?> FindCommunicationByIdentifierAsync(string patientId, string system, string value, CancellationToken ct)
    {
        Record("Communication?identifier&subject", patientId);
        return Task.FromResult(FindOwned(Communications, c => c.Subject, c => c.Identifier, patientId, system, value));
    }

    public Task<Communication> CreateCommunicationAsync(Communication resource, CancellationToken ct)
    {
        Record("Communication create", PatientIdOf(resource.Subject));
        return Task.FromResult(Store(resource));
    }

    private static T? FindOwned<T>(IEnumerable<T> resources, Func<T, ResourceReference?> subject, Func<T, List<Identifier>> identifiers,
        string patientId, string system, string value) where T : Resource =>
        resources
            .Where(r => subject(r)?.Reference == $"Patient/{patientId}" && identifiers(r).Any(i => i.System == system && i.Value == value))
            .Select(r => (T)r.DeepCopy())
            .FirstOrDefault();

    private T Store<T>(T resource) where T : Resource
    {
        var copy = (T)resource.DeepCopy();
        copy.Id = Guid.NewGuid().ToString("N");
        _store[Key(copy)] = copy;
        return (T)copy.DeepCopy();
    }

    private void Record(string operation, string? patientId)
    {
        if (Fault is { } fault)
        {
            throw fault();
        }
        Calls.Enqueue((operation, patientId));
    }

    private static string? PatientIdOf(ResourceReference? reference) =>
        reference?.Reference is { } r && r.StartsWith("Patient/", StringComparison.Ordinal) ? r["Patient/".Length..] : null;

    private static string Key(Resource r) => $"{r.TypeName}/{r.Id}";
}
