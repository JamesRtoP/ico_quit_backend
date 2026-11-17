using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace MedApp.Api.Fhir;

/// <summary>
/// All FHIR access goes through this interface. Every method that reads patient data takes the caller's Patient id
/// and MUST constrain the FHIR query with <c>subject=Patient/{patientId}</c>. Creates take a resource whose
/// <c>subject</c> the caller has already set server-side.
/// Implementation: <see cref="FirelyFhirGateway"/> (Azure Health Data Services FHIR service).
/// </summary>
public interface IFhirGateway
{
    /// <summary>Patient ids whose identifier matches <paramref name="system"/>|<paramref name="value"/>.</summary>
    Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct);

    /// <summary>The patient's Observation with this identifier, or null. Used to make offline re-sends idempotent.</summary>
    Task<Observation?> FindObservationByIdentifierAsync(string patientId, string system, string value, CancellationToken ct);

    /// <summary>The patient's Observations with this code and effective time in [rangeStart, rangeEnd], newest first.</summary>
    Task<IReadOnlyList<Observation>> SearchObservationsAsync(
        string patientId, string codeSystem, string code, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct);

    /// <summary>Creates the Observation. Callers set <c>subject</c> server-side before calling.</summary>
    Task<Observation> CreateObservationAsync(Observation resource, CancellationToken ct);

    /// <summary>The patient's Communication with this identifier, or null. Used to make offline re-sends idempotent.</summary>
    Task<Communication?> FindCommunicationByIdentifierAsync(string patientId, string system, string value, CancellationToken ct);

    /// <summary>Creates the Communication. Callers set <c>subject</c> and <c>sender</c> server-side before calling.</summary>
    Task<Communication> CreateCommunicationAsync(Communication resource, CancellationToken ct);
}
