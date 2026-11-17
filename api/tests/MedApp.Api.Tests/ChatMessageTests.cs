using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hl7.Fhir.Model;
using MedApp.Api.Tests.Support;
using MedApp.Api.Users;
using Task = System.Threading.Tasks.Task;

namespace MedApp.Api.Tests;

/// <summary>/user/chat-messages: the patient's messages stored as FHIR Communications. Relay to the chatbot is a stub.</summary>
public sealed class ChatMessageTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    private static object Message(string id, string text = "Synthetic message.") =>
        new { clientMessageId = id, text, sentAt = DateTimeOffset.UtcNow.AddMinutes(-2) };

    private List<Communication> Stored(string clientMessageId) =>
        backend.Gateway.Communications.Where(c => c.Identifier.Any(i => i.Value == clientMessageId)).ToList();

    [Fact]
    public async Task Send_Returns201_StoresCommunicationFromCaller_AndDoesNotEchoText()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        var id = Guid.NewGuid().ToString();

        var response = await client.PostAsJsonAsync("/user/chat-messages", new
        {
            clientMessageId = id, text = "I had a craving after lunch.", sentAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            subject = new { reference = $"Patient/{SeedData.PatientB}" }, patientId = SeedData.PatientB,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("craving", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var stored = Assert.Single(Stored(id));
        Assert.Equal($"Patient/{SeedData.PatientA}", stored.Subject?.Reference);
        Assert.Equal($"Patient/{SeedData.PatientA}", stored.Sender?.Reference);
        Assert.Equal("I had a craving after lunch.", (Assert.Single(stored.Payload).Content as FhirString)?.Value);
        Assert.Contains(stored.Category.SelectMany(c => c.Coding), c => c.Code == ChatMessages.CategoryCode);
    }

    [Fact]
    public async Task Send_SameClientMessageId_IsIdempotent()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        var id = Guid.NewGuid().ToString();

        var first = await client.PostAsJsonAsync("/user/chat-messages", Message(id));
        var second = await client.PostAsJsonAsync("/user/chat-messages", Message(id));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString(),
            (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
        Assert.Single(Stored(id));
    }

    public static TheoryData<string> InvalidBodies()
    {
        var ok = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture);
        var future = DateTimeOffset.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture);
        var id = Guid.NewGuid();
        return new TheoryData<string>
        {
            $$"""{"text":"hi","sentAt":"{{ok}}"}""",                                        // missing id
            $$"""{"clientMessageId":"nope","text":"hi","sentAt":"{{ok}}"}""",
            $$"""{"clientMessageId":"{{id}}","text":"","sentAt":"{{ok}}"}""",
            $$"""{"clientMessageId":"{{id}}","text":"   ","sentAt":"{{ok}}"}""",
            $$"""{"clientMessageId":"{{id}}","text":"{{new string('x', 4001)}}","sentAt":"{{ok}}"}""",
            $$"""{"clientMessageId":"{{id}}","text":"hi","sentAt":"{{future}}"}""",
            $$"""{"clientMessageId":"{{id}}","text":"hi"}""",                                // missing time
            """{ not json""",
        };
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Send_InvalidBody_Returns400_AndWritesNothing(string json)
    {
        var before = backend.Gateway.Communications.Count();
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/user/chat-messages", content)).StatusCode);
        Assert.Equal(before, backend.Gateway.Communications.Count());
    }
}
