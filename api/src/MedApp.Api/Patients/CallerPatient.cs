using MedApp.Api.Auth;
using MedApp.Api.Fhir;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MedApp.Api.Patients;

/// <summary>
/// The FHIR Patient id of the authenticated caller for the current request.
/// Populated only by <see cref="CallerPatientFilter"/> from the validated token's <c>oid</c> claim —
/// never from the URL, query string or body (HIPAA rule 1).
/// </summary>
public sealed class CallerPatient
{
    private string? _patientId;

    public string PatientId => _patientId
        ?? throw new InvalidOperationException("Caller patient has not been resolved for this request.");

    internal void Set(string patientId) => _patientId = patientId;
}

public interface ICallerPatientResolver
{
    /// <summary>Returns the caller's Patient id, or null if there is no single linked Patient.</summary>
    Task<string?> ResolveAsync(string objectId, CancellationToken ct);
}

internal sealed partial class CallerPatientResolver(IFhirGateway fhir, ILogger<CallerPatientResolver> logger) : ICallerPatientResolver
{
    public const string ExternalIdSystem = "https://medapp.example/external-id";

    public async Task<string?> ResolveAsync(string objectId, CancellationToken ct)
    {
        var ids = await fhir.FindPatientIdsByIdentifierAsync(ExternalIdSystem, objectId, ct).ConfigureAwait(false);

        switch (ids.Count)
        {
            case 1:
                return ids[0];
            case 0:
                LogNoPatient(logger);
                return null;
            default:
                // Ambiguous link is a data-integrity problem; fail closed.
                LogAmbiguous(logger, ids.Count);
                return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authenticated caller has no linked Patient")]
    private static partial void LogNoPatient(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Authenticated caller is linked to {Count} Patients; denying access")]
    private static partial void LogAmbiguous(ILogger logger, int count);
}

/// <summary>
/// Endpoint filter for the <c>/me</c> group: maps the token's oid to a FHIR Patient and stores it in
/// <see cref="CallerPatient"/>. Returns 403 when the caller can't be mapped to exactly one Patient.
/// </summary>
internal sealed class CallerPatientFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var objectId = http.User.GetCallerObjectId();
        if (objectId is null)
        {
            return Forbidden();
        }

        var resolver = http.RequestServices.GetRequiredService<ICallerPatientResolver>();
        var patientId = await resolver.ResolveAsync(objectId, http.RequestAborted).ConfigureAwait(false);
        if (patientId is null)
        {
            return Forbidden();
        }

        http.RequestServices.GetRequiredService<CallerPatient>().Set(patientId);
        return await next(context).ConfigureAwait(false);
    }

    private static ProblemHttpResult Forbidden() =>
        TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied.");
}
