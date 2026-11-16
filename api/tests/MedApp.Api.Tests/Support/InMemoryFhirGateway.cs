using System.Collections.Concurrent;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MedApp.Api.Fhir;
using MedApp.Api.Medications;
using Task = System.Threading.Tasks.Task;

namespace MedApp.Api.Tests.Support;

/// <summary>
/// In-memory FHIR store loaded from the seed bundle. Mimics FHIR search semantics for the queries the
/// API makes, and records which patient each call was scoped to.
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
        foreach (var entry in bundle.Entry.Where(e => e.Resource is not null))
        {
            gateway._store[Key(entry.Resource!)] = entry.Resource!;
        }
        return gateway;
    }

    public IEnumerable<MedicationAdministration> Administrations => _store.Values.OfType<MedicationAdministration>();

    public Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct)
    {
        Record("Patient?identifier", null);
        IReadOnlyList<string> ids = _store.Values.OfType<Patient>()
            .Where(p => p.Identifier.Any(i => i.System == system && i.Value == value))
            .Select(p => p.Id!)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<IReadOnlyList<MedicationRequest>> SearchActiveMedicationRequestsAsync(string patientId, CancellationToken ct)
    {
        Record("MedicationRequest?subject&status=active", patientId);
        IReadOnlyList<MedicationRequest> result = _store.Values.OfType<MedicationRequest>()
            .Where(r => Subject(r.Subject) == patientId && r.Status == MedicationRequest.MedicationrequestStatus.Active)
            .Select(r => (MedicationRequest)r.DeepCopy())
            .ToList();
        return Task.FromResult(result);
    }

    public Task<MedicationRequest?> FindMedicationRequestAsync(string patientId, string medicationRequestId, CancellationToken ct)
    {
        Record("MedicationRequest?_id&subject", patientId);
        var match = _store.Values.OfType<MedicationRequest>()
            .FirstOrDefault(r => r.Id == medicationRequestId && Subject(r.Subject) == patientId);
        return Task.FromResult((MedicationRequest?)match?.DeepCopy());
    }

    public Task<IReadOnlyList<MedicationAdministration>> SearchMedicationAdministrationsAsync(
        string patientId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
    {
        Record("MedicationAdministration?subject&effective-time", patientId);
        IReadOnlyList<MedicationAdministration> result = Administrations
            .Where(a => Subject(a.Subject) == patientId)
            .Select(a => (Admin: a, When: (a.Effective as FhirDateTime)?.ToDateTimeOffset(TimeSpan.Zero)))
            .Where(x => x.When is { } w && w >= rangeStart && w <= rangeEnd)
            .OrderByDescending(x => x.When)
            .Select(x => (MedicationAdministration)x.Admin.DeepCopy())
            .ToList();
        return Task.FromResult(result);
    }

    public Task<MedicationAdministration> CreateMedicationAdministrationAsync(MedicationAdministration resource, CancellationToken ct)
    {
        Record("MedicationAdministration create", Subject(resource.Subject));
        var copy = (MedicationAdministration)resource.DeepCopy();
        copy.Id = Guid.NewGuid().ToString("N");
        _store[Key(copy)] = copy;
        return Task.FromResult((MedicationAdministration)copy.DeepCopy());
    }

    private void Record(string operation, string? patientId)
    {
        if (Fault is { } fault)
        {
            throw fault();
        }
        Calls.Enqueue((operation, patientId));
    }

    private static string? Subject(ResourceReference? reference) => FhirMapping.ReferenceId(reference, "Patient");

    private static string Key(Resource r) => $"{r.TypeName}/{r.Id}";
}
