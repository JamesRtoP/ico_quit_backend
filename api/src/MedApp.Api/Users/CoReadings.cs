using Hl7.Fhir.Model;
using MedApp.Api.Fhir;
using MedApp.Api.Patients;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MedApp.Api.Users;

/// <summary>Whether the iCO device reported a stable reading.</summary>
public enum CoReadingReliability
{
    /// <summary>SDK status <c>SUCCESS</c>.</summary>
    Stable,

    /// <summary>SDK status <c>SUCCESS_FAILED_TO_STABILISE</c>: a value was returned but is not reliable.</summary>
    Unstable,
}

/// <summary>An exhaled-CO reading from the iCO Smokerlyzer, taken by the signed-in patient.</summary>
public sealed class RecordCoReadingRequest
{
    /// <summary>A GUID the app generates once per reading. Re-sending the same id (offline sync retry) does not duplicate it.</summary>
    public required string ClientReadingId { get; init; }

    /// <summary>The reading in ppm, as returned by <c>ReadingComplete.OnComplete</c>.</summary>
    public required int Ppm { get; init; }

    /// <summary><c>stable</c> for SDK <c>SUCCESS</c>, <c>unstable</c> for <c>SUCCESS_FAILED_TO_STABILISE</c>. Do not send failed tests.</summary>
    public required CoReadingReliability Reliability { get; init; }

    /// <summary>When the breath test completed, on the phone (may be in the past if the reading was queued offline).</summary>
    public required DateTimeOffset MeasuredAt { get; init; }
}

/// <summary>A stored CO reading.</summary>
public sealed record CoReadingDto(string Id, string? ClientReadingId, int? Ppm, CoReadingReliability Reliability, DateTimeOffset? MeasuredAt);

/// <summary>
/// <c>/user/co-readings</c>: exhaled breath CO readings from the Bedfont iCO Smokerlyzer, stored as FHIR Observations
/// for the caller. A reading tied to a patient is PHI: values are never logged. The device serial number (a HIPAA
/// identifier) is deliberately not accepted.
/// </summary>
internal static class CoReadings
{
    // PLACEHOLDER code until the client confirms the LOINC code for exhaled CO (see docs/decisions.md D14).
    public const string CodeSystem = "https://medapp.example/codes";
    public const string Code = "exhaled-co";
    public const string IdentifierSystem = "https://medapp.example/co-reading-id";

    // Reported range of the iCO Smokerlyzer; confirm with Bedfont/client.
    public const int MinPpm = 0;
    public const int MaxPpm = 500;

    private static readonly TimeSpan DefaultHistoryWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaxHistoryWindow = TimeSpan.FromDays(366);

    public static RouteGroupBuilder MapCoReadingEndpoints(this RouteGroupBuilder user)
    {
        user.MapPost("/co-readings", Record)
            .WithName("RecordCoReading")
            .WithSummary("Record an exhaled-CO reading from the iCO device for the signed-in patient. Idempotent on clientReadingId.")
            .ProducesValidationProblem();

        user.MapGet("/co-readings", History)
            .WithName("GetCoReadings")
            .WithSummary("The signed-in patient's CO readings, newest first. Defaults to the last 30 days; at most 366 days per request.")
            .ProducesValidationProblem();

        return user;
    }

    internal static async Task<Results<Created<CoReadingDto>, Ok<CoReadingDto>, ValidationProblem>> Record(
        RecordCoReadingRequest request, CallerPatient caller, IFhirGateway fhir, TimeProvider clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Guid.TryParse(request.ClientReadingId, out var readingId))
        {
            errors["clientReadingId"] = ["Must be a GUID."];
        }
        if (request.Ppm is < MinPpm or > MaxPpm)
        {
            errors["ppm"] = [$"Must be between {MinPpm} and {MaxPpm}."];
        }
        if (!Enum.IsDefined(request.Reliability))
        {
            errors["reliability"] = ["Must be 'stable' or 'unstable'."];
        }
        if (ClientTimestamps.Validate(request.MeasuredAt, clock.GetUtcNow()) is { } timeError)
        {
            errors["measuredAt"] = [timeError];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var identifier = readingId.ToString("D");
        var existing = await fhir.FindObservationByIdentifierAsync(caller.PatientId, IdentifierSystem, identifier, ct).ConfigureAwait(false);
        if (existing is not null && IsOwnedBy(existing, caller.PatientId))
        {
            return TypedResults.Ok(ToDto(existing)); // already synced
        }

        var observation = new Observation
        {
            Identifier = [new Identifier(IdentifierSystem, identifier)],
            // Unstable readings are kept but marked preliminary, so they are never mistaken for verified results.
            Status = request.Reliability == CoReadingReliability.Stable
                ? ObservationStatus.Final
                : ObservationStatus.Preliminary,
            Code = new CodeableConcept(CodeSystem, Code, "Exhaled breath carbon monoxide"),
            Subject = new ResourceReference($"Patient/{caller.PatientId}"), // server-side, never from the request
            Effective = new FhirDateTime(request.MeasuredAt.ToUniversalTime()),
            Value = new Quantity(request.Ppm, "[ppm]", "http://unitsofmeasure.org") { Unit = "ppm" },
        };

        var created = await fhir.CreateObservationAsync(observation, ct).ConfigureAwait(false);
        return TypedResults.Created((string?)null, ToDto(created));
    }

    internal static async Task<Results<Ok<List<CoReadingDto>>, ValidationProblem>> History(
        CallerPatient caller, IFhirGateway fhir, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var rangeEnd = to ?? clock.GetUtcNow();
        var rangeStart = from ?? rangeEnd - DefaultHistoryWindow;
        if (rangeStart > rangeEnd)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Must not be after 'to'."] });
        }
        if (rangeEnd - rangeStart > MaxHistoryWindow)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Range must be at most 366 days."] });
        }

        var found = await fhir.SearchObservationsAsync(caller.PatientId, CodeSystem, Code, rangeStart, rangeEnd, ct).ConfigureAwait(false);
        // Defence in depth: the search is already scoped to the caller; drop anything that isn't.
        return TypedResults.Ok(found.Where(o => IsOwnedBy(o, caller.PatientId)).Select(ToDto).ToList());
    }

    private static bool IsOwnedBy(Observation o, string patientId) => o.Subject?.Reference == $"Patient/{patientId}";

    private static CoReadingDto ToDto(Observation o) => new(
        o.Id ?? "",
        o.Identifier.FirstOrDefault(i => i.System == IdentifierSystem)?.Value,
        (o.Value as Quantity)?.Value is { } v ? (int)v : null,
        o.Status == ObservationStatus.Final ? CoReadingReliability.Stable : CoReadingReliability.Unstable,
        (o.Effective as FhirDateTime)?.ToDateTimeOffset(TimeSpan.Zero));
}
