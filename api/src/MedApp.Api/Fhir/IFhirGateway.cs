namespace MedApp.Api.Fhir;

/// <summary>
/// All FHIR access goes through this interface. Any method added later that reads or writes patient data
/// MUST take the caller's Patient id and constrain the FHIR query with <c>subject=Patient/{patientId}</c>.
/// Implementation: <see cref="FirelyFhirGateway"/> (Azure Health Data Services FHIR service).
/// </summary>
public interface IFhirGateway
{
    /// <summary>Patient ids whose identifier matches <paramref name="system"/>|<paramref name="value"/>.</summary>
    Task<IReadOnlyList<string>> FindPatientIdsByIdentifierAsync(string system, string value, CancellationToken ct);
}
