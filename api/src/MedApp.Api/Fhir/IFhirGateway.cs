using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace MedApp.Api.Fhir;

/// <summary>
/// All FHIR access goes through this interface. Every patient-data method takes the caller's
/// Patient id and MUST constrain the FHIR query with <c>subject=Patient/{patientId}</c>.
/// Implementations: <see cref="FirelyFhirGateway"/> (Azure Health Data Services FHIR service).
/// </summary>
public interface IFhirGateway
{
    /// <summary>Patient ids whose identifier matches <paramref name="system"/>|<paramref name="value"/>.</summary>
    Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct);

    /// <summary>Active MedicationRequests for the patient.</summary>
    Task<IReadOnlyList<MedicationRequest>> SearchActiveMedicationRequestsAsync(string patientId, CancellationToken ct);

    /// <summary>The MedicationRequest with this id, only if it belongs to the patient; otherwise null.</summary>
    Task<MedicationRequest?> FindMedicationRequestAsync(string patientId, string medicationRequestId, CancellationToken ct);

    /// <summary>MedicationAdministrations for the patient with effective time in [rangeStart, rangeEnd], newest first.</summary>
    Task<IReadOnlyList<MedicationAdministration>> SearchMedicationAdministrationsAsync(
        string patientId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct);

    /// <summary>Creates the MedicationAdministration. Callers set <c>subject</c> server-side before calling.</summary>
    Task<MedicationAdministration> CreateMedicationAdministrationAsync(MedicationAdministration resource, CancellationToken ct);
}
