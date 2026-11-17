using System.Net;
using System.Net.Http.Json;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

/// <summary>Every /user endpoint needs a valid token whose oid links to exactly one Patient, and never logs PHI.</summary>
public sealed class UserAccessTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    public static TheoryData<string, string> UserEndpoints => new()
    {
        { "POST", "/user/co-readings" },
        { "GET", "/user/co-readings" },
        { "POST", "/user/chat-messages" },
    };

    [Theory]
    [MemberData(nameof(UserEndpoints))]
    public async Task NoToken_Returns401_WithoutTouchingFhir(string method, string path)
    {
        var before = backend.Gateway.Calls.Count;
        using var client = backend.Factory.Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, method, path)).StatusCode);
        Assert.Equal(before, backend.Gateway.Calls.Count);
    }

    [Theory]
    [MemberData(nameof(UserEndpoints))]
    public async Task UnlinkedOrMissingOid_Returns403_AndWritesNothing(string method, string path)
    {
        var observations = backend.Gateway.Observations.Count();
        var communications = backend.Gateway.Communications.Count();

        foreach (var oid in new[] { SeedData.OidUnlinked, null })
        {
            using var client = backend.Factory.ClientFor(oid);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, method, path)).StatusCode);
        }

        Assert.Equal(observations, backend.Gateway.Observations.Count());
        Assert.Equal(communications, backend.Gateway.Communications.Count());
    }

    [Fact]
    public async Task Logs_NeverContainPhi()
    {
        using var isolated = new InMemoryBackend();
        using var client = isolated.Factory.ClientFor(SeedData.OidA);
        using var unlinked = isolated.Factory.ClientFor(SeedData.OidUnlinked);

        await client.PostAsJsonAsync("/user/co-readings", new
        {
            clientReadingId = Guid.NewGuid(), ppm = 37, reliability = "stable", measuredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await client.GetAsync("/user/co-readings");
        await client.PostAsJsonAsync("/user/chat-messages", new
        {
            clientMessageId = Guid.NewGuid(), text = "Avery Quillfeather born 1971-03-14 here", sentAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await client.PostAsJsonAsync("/user/co-readings", new { clientReadingId = "bad", ppm = 9999 }); // 400
        await unlinked.GetAsync("/user/co-readings"); // 403

        var logs = isolated.Factory.Logs.AllText;
        Assert.NotEmpty(isolated.Factory.Logs.Lines);
        foreach (var sensitive in SeedData.SensitiveStrings)
        {
            Assert.DoesNotContain(sensitive, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string path) =>
        method == "POST"
            ? client.PostAsJsonAsync(path, new
            {
                clientReadingId = Guid.NewGuid(), clientMessageId = Guid.NewGuid(), ppm = 10, reliability = "stable",
                text = "Synthetic.", measuredAt = DateTimeOffset.UtcNow.AddMinutes(-1), sentAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            })
            : client.GetAsync(path);
}
