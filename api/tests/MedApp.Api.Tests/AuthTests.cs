using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

/// <summary>
/// /chatbot requires a delegated (on-behalf-of) token: issued to the chatbot client (azp) AND carrying the oid of a
/// patient linked to exactly one FHIR Patient. Every other token is rejected before any patient data is touched.
/// </summary>
public sealed class AuthTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    public static TheoryData<string, string> ChatbotEndpoints => new()
    {
        { "GET", "/chatbot/context" },
        { "POST", "/chatbot/responses" },
    };

    [Fact]
    public async Task Status_IsAnonymous_AndReturnsNoData()
    {
        using var client = backend.Factory.Client();

        var response = await client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [MemberData(nameof(ChatbotEndpoints))]
    public async Task NoToken_Returns401(string method, string path)
    {
        var before = backend.Gateway.Calls.Count;
        using var client = backend.Factory.Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, method, path)).StatusCode);
        Assert.Equal(before, backend.Gateway.Calls.Count); // FHIR never touched
    }

    [Theory]
    [MemberData(nameof(ChatbotEndpoints))]
    public async Task ExpiredToken_Returns401(string method, string path)
    {
        using var client = backend.Factory.Client(backend.Factory.Tokens.For(
            SeedData.OidA, lifetime: TimeSpan.FromMinutes(-5), clientId: MedAppFactory.ChatbotClientId));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, method, path)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(ChatbotEndpoints))]
    public async Task TokenSignedWithWrongKey_Returns401(string method, string path)
    {
        using var client = backend.Factory.Client(backend.Factory.Tokens.For(
            SeedData.OidA, signingKey: RandomNumberGenerator.GetBytes(32), clientId: MedAppFactory.ChatbotClientId));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, method, path)).StatusCode);
    }

    [Fact]
    public async Task MalformedBearer_Returns401()
    {
        using var client = backend.Factory.Client("not-a-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/chatbot/context")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(ChatbotEndpoints))]
    public async Task PatientToken_NotIssuedToChatbot_Returns403_WithoutTouchingFhir(string method, string path)
    {
        var before = backend.Gateway.Calls.Count;
        using var client = backend.Factory.ClientFor(SeedData.OidA); // a patient's own token, no azp

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, method, path)).StatusCode);
        Assert.Equal(before, backend.Gateway.Calls.Count);
    }

    [Fact]
    public async Task TokenIssuedToAnotherClient_Returns403()
    {
        using var client = backend.Factory.Client(backend.Factory.Tokens.For(SeedData.OidA, clientId: "some-other-app"));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/chatbot/context")).StatusCode);
    }

    [Theory]
    [InlineData(SeedData.OidUnlinked)] // e.g. an app-only chatbot token: its oid is a service principal, not a patient
    [InlineData(null)]                  // no oid at all
    public async Task ChatbotToken_WithoutLinkedPatient_Returns403(string? oid)
    {
        using var client = backend.Factory.ChatbotClient(oid);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/chatbot/context")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(ChatbotEndpoints))]
    public async Task DelegatedChatbotToken_PassesAuth_AndStubReturns501(string method, string path)
    {
        using var client = backend.Factory.ChatbotClient();

        Assert.Equal(HttpStatusCode.NotImplemented, (await Send(client, method, path)).StatusCode);
    }

    internal static Task<HttpResponseMessage> Send(HttpClient client, string method, string path) =>
        method == "POST"
            ? client.PostAsJsonAsync(path, new { text = "Synthetic chatbot reply." })
            : client.GetAsync(path);
}
