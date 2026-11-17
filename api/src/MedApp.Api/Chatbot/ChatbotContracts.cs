namespace MedApp.Api.Chatbot;

/// <summary>A response the chatbot gave the signed-in patient, submitted for storage.</summary>
public sealed class ChatbotResponseRequest
{
    /// <summary>The response text, up to 4000 characters. May contain PHI: it is never logged.</summary>
    public required string Text { get; init; }
}
