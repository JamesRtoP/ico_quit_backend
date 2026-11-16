using Hl7.Fhir.Model;

namespace MedApp.Api.Medications;

internal static class FhirMapping
{
    public const string RxNormSystem = "http://www.nlm.nih.gov/research/umls/rxnorm";

    public static MedicationDto ToDto(this MedicationRequest request)
    {
        CodingDto? code = null;
        string? name = null;

        switch (request.Medication)
        {
            case CodeableConcept concept:
                var coding = concept.Coding.FirstOrDefault(c => c.System == RxNormSystem) ?? concept.Coding.FirstOrDefault();
                if (coding is not null)
                {
                    code = new CodingDto(coding.System, coding.Code, coding.Display);
                }
                name = concept.Text ?? coding?.Display;
                break;
            case ResourceReference reference:
                name = reference.Display;
                break;
        }

        var schedule = request.DosageInstruction
            .Select(d => d.Timing).OfType<Timing>().Select(ToSchedule)
            .ToList();

        return new MedicationDto(
            Id: request.Id ?? "",
            Name: name,
            Code: code,
            Status: request.Status?.ToString().ToLowerInvariant() ?? "unknown",
            DosageText: request.DosageInstruction.FirstOrDefault()?.Text,
            Schedule: schedule,
            AuthoredOn: TryParse(request.AuthoredOnElement));
    }

    public static DoseDto ToDto(this MedicationAdministration administration) =>
        new(
            Id: administration.Id ?? "",
            MedicationRequestId: ReferenceId(administration.Request, "MedicationRequest"),
            Status: administration.Status == MedicationAdministration.MedicationAdministrationStatusCodes.NotDone
                ? DoseStatus.Skipped
                : DoseStatus.Taken,
            TakenAt: TryParse(administration.Effective as FhirDateTime));

    /// <summary>"Patient/123" (or an absolute URL ending in it) → "123" when the type matches.</summary>
    public static string? ReferenceId(ResourceReference? reference, string resourceType)
    {
        var value = reference?.Reference;
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Ignore version-specific references' "_history/n" suffix.
        var historyIndex = Array.IndexOf(parts, "_history");
        if (historyIndex >= 0)
        {
            parts = parts[..historyIndex];
        }
        return parts.Length >= 2 && parts[^2] == resourceType ? parts[^1] : null;
    }

    private static ScheduleDto ToSchedule(Timing timing)
    {
        var repeat = timing.Repeat;
        return new ScheduleDto(
            Frequency: repeat?.Frequency,
            Period: repeat?.Period,
            PeriodUnit: repeat?.PeriodUnit?.ToString().ToLowerInvariant(),
            TimeOfDay: repeat?.TimeOfDay?.OfType<string>().ToList() ?? [],
            Text: timing.Code?.Text);
    }

    private static DateTimeOffset? TryParse(FhirDateTime? value) =>
        value is not null && value.TryToDateTimeOffset(out var result) ? result : null;
}
