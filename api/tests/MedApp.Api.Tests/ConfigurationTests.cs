using System.Net;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

/// <summary>The app must fail closed when misconfigured, and expose no dev tooling outside dev.</summary>
public sealed class ConfigurationTests
{
    private static readonly Dictionary<string, string> FakeExternalId = new()
    {
        ["Auth:Mode"] = "ExternalId",
        ["ExternalId:Instance"] = "https://medapp-fake.ciamlogin.com/",
        ["ExternalId:TenantId"] = "00000000-0000-0000-0000-000000000001",
        ["ExternalId:ClientId"] = "00000000-0000-0000-0000-000000000002",
    };

    [Fact]
    public void LocalJwtMode_IsRejected_OutsideDevelopmentAndTesting()
    {
        using var factory = new MedAppFactory(gateway: InMemoryFhirGateway.FromSeed(), environment: "Production");

        var ex = Assert.ThrowsAny<Exception>(() => factory.Server);
        Assert.Contains("LocalJwt", ex.ToString());
    }

    [Fact]
    public void ExternalIdMode_WithoutClientId_FailsAtStartup()
    {
        using var factory = new MedAppFactory(gateway: InMemoryFhirGateway.FromSeed(), environment: "Production",
            extraSettings: new Dictionary<string, string> { ["Auth:Mode"] = "ExternalId", ["ExternalId:ClientId"] = "" });

        Assert.ThrowsAny<Exception>(() => factory.Server);
    }

    [Fact]
    public void MissingFhirUrl_FailsAtStartup()
    {
        using var factory = new MedAppFactory(fhirUrl: "");

        Assert.ThrowsAny<Exception>(() => factory.Server);
    }

    [Fact]
    public async Task Production_DoesNotExposeOpenApiOrSwagger_AndStatusStillWorks()
    {
        using var factory = new MedAppFactory(gateway: InMemoryFhirGateway.FromSeed(), environment: "Production", extraSettings: FakeExternalId);
        using var client = factory.Client();

        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/chatbot/context")).StatusCode);
    }

    [Fact]
    public async Task ChatbotClientIdNotConfigured_DeniesEveryChatbotCall()
    {
        using var factory = new MedAppFactory(gateway: InMemoryFhirGateway.FromSeed(),
            extraSettings: new Dictionary<string, string> { ["Chatbot:ClientId"] = "" });
        using var client = factory.ChatbotClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/chatbot/context")).StatusCode);
    }

    [Fact]
    public async Task Development_DoesNotServeOpenApiOrSwagger()
    {
        using var factory = new MedAppFactory(gateway: InMemoryFhirGateway.FromSeed(), environment: "Development");
        using var client = factory.ClientFor(SeedData.OidA); // even a signed-in caller gets nothing

        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/status")).StatusCode);
    }

    [Fact]
    public async Task NoCorsHeaders_AreEmitted()
    {
        using var factory = new MedAppFactory(gateway: InMemoryFhirGateway.FromSeed());
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/chatbot/context");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
