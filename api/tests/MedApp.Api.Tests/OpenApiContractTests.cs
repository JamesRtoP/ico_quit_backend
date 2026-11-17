using System.Text.Json;
using System.Text.Json.Nodes;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

/// <summary>
/// The OpenAPI document is a deliverable for the mobile and chatbot teams (Foundry imports it as an OpenAPI
/// tool). It is generated only in the Testing environment and snapshotted at docs/openapi.json; an API change
/// that alters the contract fails this test until the snapshot is regenerated deliberately:
/// set UPDATE_OPENAPI_SNAPSHOT=1 and run the tests.
/// </summary>
public sealed class OpenApiContractTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    [Fact]
    public async Task Contract_ExposesChatbotEndpoints_WithBearerAuth()
    {
        using var client = backend.Factory.Client();
        var doc = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;

        var paths = doc["paths"]!.AsObject().Select(p => p.Key).Order().ToList();
        Assert.Equal(["/chatbot/context", "/chatbot/responses"], paths);
        Assert.NotNull(doc["components"]?["securitySchemes"]?["Bearer"]);

        // No endpoint takes a patient identifier from the client.
        var text = doc.ToJsonString();
        Assert.DoesNotContain("\"patientId\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"subject\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"userId\"", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contract_MatchesSnapshot()
    {
        using var client = backend.Factory.Client();
        var actual = Normalize(await client.GetStringAsync("/openapi/v1.json"));
        var snapshotPath = Path.Combine(RepoRoot(), "docs", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            await File.WriteAllTextAsync(snapshotPath, actual);
            return;
        }

        Assert.True(File.Exists(snapshotPath), "docs/openapi.json missing. Run tests with UPDATE_OPENAPI_SNAPSHOT=1 to create it.");
        var expected = Normalize(await File.ReadAllTextAsync(snapshotPath));
        Assert.True(expected == actual,
            "The OpenAPI contract changed. If intended, run tests with UPDATE_OPENAPI_SNAPSHOT=1 and commit docs/openapi.json.");
    }

    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Repo root (global.json) not found.");
    }
}
