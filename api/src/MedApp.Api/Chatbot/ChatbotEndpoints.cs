using MedApp.Api.Auth;
using MedApp.Api.Patients;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MedApp.Api.Chatbot;

/// <summary>
/// <c>/chatbot/*</c>: endpoints the client's Foundry chatbot calls on behalf of the patient who is chatting.
/// <para>
/// The chatbot exchanges the patient's sign-in token for a token to this API (OAuth 2.0 on-behalf-of flow).
/// That one token carries both identities, and the API requires both:
/// <list type="bullet">
/// <item>the patient: <c>oid</c>, resolved to their FHIR Patient by <see cref="CallerPatientFilter"/>, exactly like <c>/user</c>;</item>
/// <item>the chatbot: <c>azp</c>/<c>appid</c> must equal <c>Chatbot:ClientId</c> (<see cref="AuthenticationSetup.ChatbotPolicy"/>).</item>
/// </list>
/// No request carries a patient id, so neither the chatbot nor its model can choose which patient is accessed
/// (HIPAA rule 1).
/// </para>
/// <para>STUBS: both endpoints check auth and input, then return 501. They read and store no FHIR data yet.</para>
/// </summary>
internal static class ChatbotEndpoints
{
    public const int MaxResponseLength = 4000;

    public static RouteGroupBuilder MapChatbotEndpoints(this IEndpointRouteBuilder app)
    {
        var chatbot = app.MapGroup("/chatbot")
            .RequireAuthorization(AuthenticationSetup.ChatbotPolicy)
            .AddEndpointFilter<CallerPatientFilter>()
            .WithTags("Chatbot")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        chatbot.MapGet("/context", GetPatientContext)
            .WithName("GetChatbotPatientContext")
            .WithSummary("STUB. Intent: return the FHIR information about the signed-in patient that the chatbot needs to answer them.")
            .WithDescription("Not implemented: returns 501 once auth passes. The patient is taken from the delegated token, never from the request.");

        chatbot.MapPost("/responses", SubmitResponse)
            .WithName("SubmitChatbotResponse")
            .WithSummary("STUB. Intent: store the response the chatbot gave the signed-in patient (e.g. as a FHIR Communication).")
            .WithDescription("Not implemented: returns 501 once auth and input checks pass. Nothing is stored.")
            .ProducesValidationProblem();

        return chatbot;
    }

    internal static ProblemHttpResult GetPatientContext(CallerPatient caller)
    {
        // TODO: read the agreed data from FHIR, scoped with subject=Patient/{caller.PatientId}.
        _ = caller.PatientId;
        return NotImplementedYet();
    }

    internal static Results<ValidationProblem, ProblemHttpResult> SubmitResponse(CallerPatient caller, ChatbotResponseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxResponseLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["text"] = [$"Required, at most {MaxResponseLength} characters."],
            });
        }

        // TODO: store in FHIR with subject = Patient/{caller.PatientId}, set server-side.
        _ = caller.PatientId;
        return NotImplementedYet();
    }

    private static ProblemHttpResult NotImplementedYet() =>
        TypedResults.Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Not implemented yet.");
}
