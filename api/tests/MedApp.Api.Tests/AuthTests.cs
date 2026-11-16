using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

public sealed class AuthTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    public static TheoryData<string, string> MeEndpoints => new()
    {
        { "GET", "/me/medications" },
        { "GET", "/me/doses" },
        { "GET", "/me/doses?from=2026-11-01T00:00:00Z&to=2026-11-02T00:00:00Z" },
        { "POST", "/me/doses" },
    };

    [Fact]
    public async Task Healthz_IsAnonymous_AndReturnsNoData()
    {
        using var client = backend.Factory.Client();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [MemberData(nameof(MeEndpoints))]
    public async Task NoToken_Returns401(string method, string path)
    {
        var before = backend.Gateway.Calls.Count;
        using var client = backend.Factory.Client();

        var response = await Send(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, backend.Gateway.Calls.Count); // FHIR never touched
    }

    [Theory]
    [MemberData(nameof(MeEndpoints))]
    public async Task ExpiredToken_Returns401(string method, string path)
    {
        using var client = backend.Factory.Client(backend.Factory.Tokens.For(SeedData.OidA, lifetime: TimeSpan.FromMinutes(-5)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, method, path)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(MeEndpoints))]
    public async Task TokenSignedWithWrongKey_Returns401(string method, string path)
    {
        using var client = backend.Factory.Client(backend.Factory.Tokens.For(SeedData.OidA, signingKey: RandomNumberGenerator.GetBytes(32)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, method, path)).StatusCode);
    }

    [Fact]
    public async Task MalformedBearer_Returns401()
    {
        using var client = backend.Factory.Client("not-a-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me/medications")).StatusCode);
    }

    [Fact]
    public async Task ValidToken_Returns200()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/me/medications")).StatusCode);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string path) =>
        method == "POST"
            ? client.PostAsJsonAsync(path, new { medicationRequestId = SeedData.A_Lisinopril, status = "taken" })
            : client.GetAsync(path);
}
