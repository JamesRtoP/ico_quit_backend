using MedApp.Api.Patients;

namespace MedApp.Api.Users;

/// <summary>
/// <c>/user/*</c>: endpoints the patient app calls with the signed-in patient's own token.
/// Every endpoint here acts on the caller only: <see cref="CallerPatientFilter"/> resolves the token's oid
/// to the caller's FHIR Patient, and no endpoint accepts a patient id (HIPAA rule 1).
/// </summary>
internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGroup("/user")
            .RequireAuthorization()
            .AddEndpointFilter<CallerPatientFilter>()
            .WithTags("User")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .MapCoReadingEndpoints()
            .MapChatMessageEndpoints();
}
