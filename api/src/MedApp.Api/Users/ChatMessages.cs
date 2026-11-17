using Hl7.Fhir.Model;
using MedApp.Api.Fhir;
using MedApp.Api.Patients;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MedApp.Api.Users;

/// <summary>A chat message the signed-in patient typed in the app.</summary>
public sealed class SendChatMessageRequest
{
    /// <summary>A GUID the app generates once per message. Re-sending the same id (offline sync retry) does not duplicate it.</summary>
    public required string ClientMessageId { get; init; }

    /// <summary>The message text, 1 to 4000 characters. PHI: it is never logged and never echoed back.</summary>
    public required string Text { get; init; }

    /// <summary>When the patient sent the message, on the phone.</summary>
    public required DateTimeOffset SentAt { get; init; }
}

/// <summary>A stored chat message (text omitted: the app already has it).</summary>
public sealed record ChatMessageDto(string Id, string? ClientMessageId, DateTimeOffset? SentAt);

/// <summary>
/// <c>/user/chat-messages</c>: the patient's chat messages, stored as FHIR Communications with the caller as subject
/// and sender. RELAY IS A STUB: the message is stored but not yet forwarded to the Foundry chatbot.
/// </summary>
internal static class ChatMessages
{
    public const int MaxTextLength = 4000;
    public const string IdentifierSystem = "https://medapp.example/chat-message-id";
    public const string CategorySystem = "https://medapp.example/codes";
    public const string CategoryCode = "patient-chat-message";

    public static RouteGroupBuilder MapChatMessageEndpoints(this RouteGroupBuilder user)
    {
        user.MapPost("/chat-messages", Send)
            .WithName("SendChatMessage")
            .WithSummary("Store a chat message from the signed-in patient. Idempotent on clientMessageId. Relay to the chatbot is not implemented yet.")
            .ProducesValidationProblem();

        return user;
    }

    internal static async Task<Results<Created<ChatMessageDto>, Ok<ChatMessageDto>, ValidationProblem>> Send(
        SendChatMessageRequest request, CallerPatient caller, IFhirGateway fhir, TimeProvider clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Guid.TryParse(request.ClientMessageId, out var messageId))
        {
            errors["clientMessageId"] = ["Must be a GUID."];
        }
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxTextLength)
        {
            errors["text"] = [$"Required, at most {MaxTextLength} characters."];
        }
        if (ClientTimestamps.Validate(request.SentAt, clock.GetUtcNow()) is { } timeError)
        {
            errors["sentAt"] = [timeError];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var identifier = messageId.ToString("D");
        var existing = await fhir.FindCommunicationByIdentifierAsync(caller.PatientId, IdentifierSystem, identifier, ct).ConfigureAwait(false);
        if (existing is not null && existing.Subject?.Reference == $"Patient/{caller.PatientId}")
        {
            return TypedResults.Ok(ToDto(existing)); // already synced
        }

        var patient = new ResourceReference($"Patient/{caller.PatientId}"); // server-side, never from the request
        var communication = new Communication
        {
            Identifier = [new Identifier(IdentifierSystem, identifier)],
            Status = EventStatus.Completed,
            Category = [new CodeableConcept(CategorySystem, CategoryCode, "Patient chat message")],
            Subject = patient,
            Sender = (ResourceReference)patient.DeepCopy(),
            SentElement = new FhirDateTime(request.SentAt.ToUniversalTime()),
            Payload = [new Communication.PayloadComponent { Content = new FhirString(request.Text) }],
        };

        var created = await fhir.CreateCommunicationAsync(communication, ct).ConfigureAwait(false);

        // TODO (stub): relay the message to the Foundry chatbot once its integration is defined.
        return TypedResults.Created((string?)null, ToDto(created));
    }

    private static ChatMessageDto ToDto(Communication c) => new(
        c.Id ?? "",
        c.Identifier.FirstOrDefault(i => i.System == IdentifierSystem)?.Value,
        c.SentElement?.ToDateTimeOffset(TimeSpan.Zero));
}
