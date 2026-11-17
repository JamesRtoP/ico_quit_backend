using System.Net;
using System.Net.Http.Json;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

/// <summary>
/// The chatbot endpoints are stubs: the patient comes only from the delegated token, input is validated, no patient
/// data is read or stored yet, and neither the patient's oid nor the response text is ever logged or echoed.
/// </summary>
public sealed class ChatbotTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    [Theory]
    [InlineData("{}")] // missing text
    [InlineData("{\"text\":\"\"}")]
    [InlineData("{\"text\":\"   \"}")]
    [InlineData("{ not json")]
    public async Task SubmitResponse_InvalidBody_Returns400(string json)
    {
        using var client = backend.Factory.ChatbotClient();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/chatbot/responses", content)).StatusCode);
    }

    [Fact]
    public async Task SubmitResponse_TooLong_Returns400()
    {
        using var client = backend.Factory.ChatbotClient();

        var response = await client.PostAsJsonAsync("/chatbot/responses", new { text = new string('x', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatientIdsInTheRequest_AreIgnored()
    {
        using var client = backend.Factory.ChatbotClient(SeedData.OidA);

        var context = await client.GetAsync($"/chatbot/context?userId={SeedData.OidB}&patient={SeedData.PatientB}");
        var response = await client.PostAsJsonAsync("/chatbot/responses",
            new { text = "Synthetic reply.", userId = SeedData.OidB, patientId = SeedData.PatientB });

        // Still authorized as patient A alone; nothing in the request can switch patients.
        Assert.Equal(HttpStatusCode.NotImplemented, context.StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task Stubs_OnlyLookUpTheCallersPatient()
    {
        backend.Gateway.Calls.Clear();
        using var client = backend.Factory.ChatbotClient();

        await client.GetAsync("/chatbot/context");
        await client.PostAsJsonAsync("/chatbot/responses", new { text = "Synthetic reply." });

        Assert.All(backend.Gateway.Calls, call => Assert.Equal("Patient?identifier", call.Operation));
    }

    [Fact]
    public async Task OidAndText_AreNeverLoggedOrEchoed()
    {
        // Own instance: other tests deliberately put ids in query strings, which request logging records.
        using var isolated = new InMemoryBackend();
        using var client = isolated.Factory.ChatbotClient();

        var context = await client.GetAsync("/chatbot/context");
        var response = await client.PostAsJsonAsync("/chatbot/responses",
            new { text = "Avery Quillfeather, born 1971-03-14, should rest today." });

        var bodies = await context.Content.ReadAsStringAsync() + await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Quillfeather", bodies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SeedData.OidA, bodies, StringComparison.OrdinalIgnoreCase);
        foreach (var sensitive in SeedData.SensitiveStrings)
        {
            Assert.DoesNotContain(sensitive, isolated.Factory.Logs.AllText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
