using Hl7.Fhir.Model;

namespace MedApp.Api.Tests.Support;

/// <summary>
/// The API hosted in-process against <see cref="InMemoryFhirGateway"/> loaded with the synthetic seed
/// bundle. Tests assert on <see cref="Gateway"/> directly to see what was actually stored.
/// </summary>
public sealed class InMemoryBackend : IDisposable
{
    public InMemoryFhirGateway Gateway { get; } = InMemoryFhirGateway.FromSeed();

    public MedAppFactory Factory { get; }

    public InMemoryBackend() => Factory = new MedAppFactory(gateway: Gateway);

    /// <summary>Total MedicationAdministrations stored for the patient.</summary>
    public int CountAdministrations(string patientId) =>
        Gateway.Administrations.Count(a => a.Subject?.Reference == $"Patient/{patientId}");

    /// <summary>A stored MedicationAdministration by id.</summary>
    public MedicationAdministration? ReadAdministration(string id) =>
        Gateway.Administrations.FirstOrDefault(a => a.Id == id);

    public void Dispose() => Factory.Dispose();
}
