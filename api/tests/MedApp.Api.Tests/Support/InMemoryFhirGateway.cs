using System.Collections.Concurrent;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MedApp.Api.Fhir;
using Task = System.Threading.Tasks.Task;

namespace MedApp.Api.Tests.Support;

/// <summary>In-memory FHIR store loaded from the seed bundle. Records every call so tests can check FHIR use.</summary>
public sealed class InMemoryFhirGateway : IFhirGateway
{
    private readonly List<Patient> _patients = [];

    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>When set, every call throws this (to exercise error paths).</summary>
    public Func<Exception>? Fault { get; set; }

    public static InMemoryFhirGateway FromSeed()
    {
        var gateway = new InMemoryFhirGateway();
        var bundle = new FhirJsonDeserializer().Deserialize<Bundle>(File.ReadAllText(SeedData.BundlePath));
        gateway._patients.AddRange(bundle.Entry.Select(e => e.Resource).OfType<Patient>());
        return gateway;
    }

    public Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct)
    {
        Record("Patient?identifier");
        IReadOnlyList<string> ids = _patients
            .Where(p => p.Identifier.Any(i => i.System == system && i.Value == value))
            .Select(p => p.Id!)
            .ToList();
        return Task.FromResult(ids);
    }

    private void Record(string operation)
    {
        if (Fault is { } fault)
        {
            throw fault();
        }
        Calls.Enqueue(operation);
    }
}
