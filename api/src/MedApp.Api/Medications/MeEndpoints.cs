using System.Text.RegularExpressions;
using Hl7.Fhir.Model;
using MedApp.Api.Fhir;
using MedApp.Api.Patients;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MedApp.Api.Medications;

/// <summary>
/// <c>/me/*</c>: the caller's own data. The patient is always resolved from the token
/// (<see cref="CallerPatientFilter"/>); no endpoint accepts a patient id.
/// </summary>
internal static partial class MeEndpoints
{
    private static readonly TimeSpan DefaultHistoryWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaxHistoryWindow = TimeSpan.FromDays(366);
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    public static RouteGroupBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me")
            .RequireAuthorization()
            .AddEndpointFilter<CallerPatientFilter>()
            .WithTags("Me")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        me.MapGet("/medications", GetMedications)
            .WithName("GetMyMedications")
            .WithSummary("List the caller's active prescriptions.");

        me.MapPost("/doses", RecordDose)
            .WithName("RecordMyDose")
            .WithSummary("Record that the caller took or skipped a dose.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("/doses", GetDoses)
            .WithName("GetMyDoses")
            .WithSummary("The caller's dose history, newest first. Defaults to the last 30 days; at most 366 days per request.")
            .ProducesValidationProblem();

        return me;
    }

    internal static async Task<Ok<List<MedicationDto>>> GetMedications(
        CallerPatient caller, IFhirGateway fhir, ILoggerFactory loggers, CancellationToken ct)
    {
        var requests = await fhir.SearchActiveMedicationRequestsAsync(caller.PatientId, ct);
        var owned = OwnedBy(requests, r => r.Subject, caller.PatientId, loggers);
        return TypedResults.Ok(owned.Select(r => r.ToDto()).ToList());
    }

    internal static async Task<Results<Created<DoseDto>, ValidationProblem, ProblemHttpResult>> RecordDose(
        RecordDoseRequest body, CallerPatient caller, IFhirGateway fhir, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var takenAt = body.TakenAt ?? now;

        var errors = new Dictionary<string, string[]>();
        if (!IsFhirId(body.MedicationRequestId))
        {
            errors["medicationRequestId"] = ["Must be a valid id."];
        }
        if (!Enum.IsDefined(body.Status))
        {
            errors["status"] = ["Must be 'taken' or 'skipped'."];
        }
        if (takenAt > now + AllowedClockSkew)
        {
            errors["takenAt"] = ["Must not be in the future."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Ownership: the MedicationRequest must belong to the caller. "Not yours" and "doesn't exist" look the same.
        var request = await fhir.FindMedicationRequestAsync(caller.PatientId, body.MedicationRequestId, ct);
        if (request is null || FhirMapping.ReferenceId(request.Subject, "Patient") != caller.PatientId)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Medication not found.");
        }
        if (request.Status != MedicationRequest.MedicationrequestStatus.Active)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Medication is not active.");
        }

        var administration = new MedicationAdministration
        {
            Status = body.Status == DoseStatus.Taken
                ? MedicationAdministration.MedicationAdministrationStatusCodes.Completed
                : MedicationAdministration.MedicationAdministrationStatusCodes.NotDone,
            // Subject is always set server-side from the resolved caller (HIPAA rule 1).
            Subject = new ResourceReference($"Patient/{caller.PatientId}"),
            Request = new ResourceReference($"MedicationRequest/{request.Id}"),
            Medication = (DataType)request.Medication.DeepCopy(),
            Effective = new FhirDateTime(takenAt.ToUniversalTime()),
        };

        var created = await fhir.CreateMedicationAdministrationAsync(administration, ct);
        return TypedResults.Created((string?)null, created.ToDto());
    }

    internal static async Task<Results<Ok<List<DoseDto>>, ValidationProblem>> GetDoses(
        CallerPatient caller, IFhirGateway fhir, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct,
        DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var end = to ?? clock.GetUtcNow();
        var start = from ?? end - DefaultHistoryWindow;

        if (start > end)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Must not be after 'to'."] });
        }
        if (end - start > MaxHistoryWindow)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["Range must be at most 366 days."] });
        }

        var administrations = await fhir.SearchMedicationAdministrationsAsync(caller.PatientId, start, end, ct);
        var owned = OwnedBy(administrations, a => a.Subject, caller.PatientId, loggers);
        return TypedResults.Ok(owned.Select(a => a.ToDto()).ToList());
    }

    /// <summary>
    /// Defense in depth: the gateway already constrains searches by subject, but never return a
    /// resource that doesn't reference the caller's Patient, whatever the gateway did.
    /// </summary>
    private static List<T> OwnedBy<T>(IEnumerable<T> resources, Func<T, ResourceReference?> subject, string patientId, ILoggerFactory loggers)
        where T : Resource
    {
        var owned = new List<T>();
        var dropped = 0;
        foreach (var resource in resources)
        {
            if (FhirMapping.ReferenceId(subject(resource), "Patient") == patientId)
            {
                owned.Add(resource);
            }
            else
            {
                dropped++;
            }
        }

        if (dropped > 0)
        {
            LogDroppedForeignResources(loggers.CreateLogger(typeof(MeEndpoints).FullName!), dropped, typeof(T).Name);
        }
        return owned;
    }

    private static bool IsFhirId(string? value) => value is not null && FhirIdRegex().IsMatch(value);

    // FHIR R4 id: [A-Za-z0-9\-\.]{1,64}
    [GeneratedRegex(@"^[A-Za-z0-9\-\.]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FhirIdRegex();

    [LoggerMessage(Level = LogLevel.Error, Message = "FHIR search returned {Count} {ResourceType} resources not owned by the caller; they were dropped")]
    private static partial void LogDroppedForeignResources(ILogger logger, int count, string resourceType);
}
