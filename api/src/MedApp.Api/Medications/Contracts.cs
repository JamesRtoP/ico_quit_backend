namespace MedApp.Api.Medications;

// Public API contract for the mobile app and the chatbot. Plain JSON, not raw FHIR.

/// <summary>A medication prescribed to the caller.</summary>
/// <param name="Id">MedicationRequest id; use it as <c>medicationRequestId</c> when recording a dose.</param>
/// <param name="Name">Display name of the drug.</param>
/// <param name="Code">Drug code (RxNorm where available).</param>
/// <param name="Status">FHIR MedicationRequest status, e.g. "active".</param>
/// <param name="DosageText">Free-text dosage instruction from the prescriber.</param>
/// <param name="Schedule">Structured timing from the dosage instructions.</param>
/// <param name="AuthoredOn">When the prescription was written.</param>
public sealed record MedicationDto(
    string Id,
    string? Name,
    CodingDto? Code,
    string Status,
    string? DosageText,
    IReadOnlyList<ScheduleDto> Schedule,
    DateTimeOffset? AuthoredOn);

public sealed record CodingDto(string? System, string? Code, string? Display);

/// <summary>When to take the medication.</summary>
/// <param name="Frequency">Number of doses per period.</param>
/// <param name="Period">Length of the period.</param>
/// <param name="PeriodUnit">UCUM-style unit: s, min, h, d, wk, mo, a.</param>
/// <param name="TimeOfDay">Times of day, "HH:mm:ss".</param>
/// <param name="Text">Human-readable timing, if supplied.</param>
public sealed record ScheduleDto(int? Frequency, decimal? Period, string? PeriodUnit, IReadOnlyList<string> TimeOfDay, string? Text);

public enum DoseStatus
{
    Taken,
    Skipped,
}

/// <summary>Records that the caller took or skipped a dose. The patient is always the caller.</summary>
public sealed class RecordDoseRequest
{
    /// <summary>Id of one of the caller's MedicationRequests (from <c>GET /me/medications</c>).</summary>
    public required string MedicationRequestId { get; init; }

    public required DoseStatus Status { get; init; }

    /// <summary>When the dose was taken or skipped. Defaults to now. Must not be in the future.</summary>
    public DateTimeOffset? TakenAt { get; init; }
}

/// <summary>A recorded dose.</summary>
public sealed record DoseDto(string Id, string? MedicationRequestId, DoseStatus Status, DateTimeOffset? TakenAt);
