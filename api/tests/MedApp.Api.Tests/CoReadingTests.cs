using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hl7.Fhir.Model;
using MedApp.Api.Tests.Support;
using MedApp.Api.Users;
using Task = System.Threading.Tasks.Task;

namespace MedApp.Api.Tests;

/// <summary>/user/co-readings: iCO breath CO readings stored as FHIR Observations for the caller only.</summary>
public sealed class CoReadingTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    private static string NewId() => Guid.NewGuid().ToString();

    private static object Reading(string id, int ppm = 12, string reliability = "stable", DateTimeOffset? measuredAt = null) =>
        new { clientReadingId = id, ppm, reliability, measuredAt = measuredAt ?? DateTimeOffset.UtcNow.AddMinutes(-3) };

    private List<Observation> Stored(string clientReadingId) =>
        backend.Gateway.Observations.Where(o => o.Identifier.Any(i => i.Value == clientReadingId)).ToList();

    [Fact]
    public async Task Record_Returns201_AndStoresObservationForCaller()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        var id = NewId();
        var measuredAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var response = await client.PostAsJsonAsync("/user/co-readings", Reading(id, ppm: 23, measuredAt: measuredAt));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, dto.GetProperty("clientReadingId").GetString());
        Assert.Equal(23, dto.GetProperty("ppm").GetInt32());
        Assert.Equal("stable", dto.GetProperty("reliability").GetString());

        var stored = Assert.Single(Stored(id));
        Assert.Equal($"Patient/{SeedData.PatientA}", stored.Subject?.Reference);
        Assert.Equal(ObservationStatus.Final, stored.Status);
        Assert.Equal(23m, (stored.Value as Quantity)?.Value);
        Assert.Equal("[ppm]", (stored.Value as Quantity)?.Code);
        Assert.Contains(stored.Code.Coding, c => c.System == CoReadings.CodeSystem && c.Code == CoReadings.Code);
    }

    [Fact]
    public async Task Record_PatientIdsInBody_AreIgnored_SubjectIsCaller()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        var id = NewId();

        var response = await client.PostAsJsonAsync("/user/co-readings", new
        {
            clientReadingId = id, ppm = 5, reliability = "stable", measuredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            subject = new { reference = $"Patient/{SeedData.PatientB}" }, patientId = SeedData.PatientB,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"Patient/{SeedData.PatientA}", Assert.Single(Stored(id)).Subject?.Reference);
    }

    [Fact]
    public async Task Record_Unstable_IsStoredAsPreliminary()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        var id = NewId();

        var response = await client.PostAsJsonAsync("/user/co-readings", Reading(id, reliability: "unstable"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ObservationStatus.Preliminary, Assert.Single(Stored(id)).Status);
        Assert.Equal("unstable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reliability").GetString());
    }

    [Fact]
    public async Task Record_SameClientReadingId_IsIdempotent()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        var id = NewId();

        var first = await client.PostAsJsonAsync("/user/co-readings", Reading(id));
        var second = await client.PostAsJsonAsync("/user/co-readings", Reading(id));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString(),
            (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
        Assert.Single(Stored(id));
    }

    [Fact]
    public async Task Record_ReusingAnotherPatientsClientReadingId_NeverReturnsTheirReading()
    {
        using var clientA = backend.Factory.ClientFor(SeedData.OidA);
        using var clientB = backend.Factory.ClientFor(SeedData.OidB);
        var id = NewId();
        await clientA.PostAsJsonAsync("/user/co-readings", Reading(id, ppm: 40));

        var response = await clientB.PostAsJsonAsync("/user/co-readings", Reading(id, ppm: 3));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode); // a new reading for B, not A's
        Assert.Equal(3, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ppm").GetInt32());
        Assert.Equal(2, Stored(id).Count);
    }

    public static TheoryData<string> InvalidBodies()
    {
        var ok = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture);
        var future = DateTimeOffset.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture);
        var ancient = DateTimeOffset.UtcNow.AddDays(-400).ToString("O", CultureInfo.InvariantCulture);
        var id = Guid.NewGuid();
        return new TheoryData<string>
        {
            $$"""{"ppm":10,"reliability":"stable","measuredAt":"{{ok}}"}""",                                   // missing id
            $$"""{"clientReadingId":"not-a-guid","ppm":10,"reliability":"stable","measuredAt":"{{ok}}"}""",
            $$"""{"clientReadingId":"{{id}}","ppm":-1,"reliability":"stable","measuredAt":"{{ok}}"}""",
            $$"""{"clientReadingId":"{{id}}","ppm":501,"reliability":"stable","measuredAt":"{{ok}}"}""",
            $$"""{"clientReadingId":"{{id}}","ppm":10,"reliability":"eaten","measuredAt":"{{ok}}"}""",
            $$"""{"clientReadingId":"{{id}}","ppm":10,"reliability":0,"measuredAt":"{{ok}}"}""",              // integer enum
            $$"""{"clientReadingId":"{{id}}","ppm":10,"reliability":"stable","measuredAt":"{{future}}"}""",
            $$"""{"clientReadingId":"{{id}}","ppm":10,"reliability":"stable","measuredAt":"{{ancient}}"}""",
            $$"""{"clientReadingId":"{{id}}","ppm":10,"reliability":"stable"}""",                           // missing time
            """{ not json""",
        };
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Record_InvalidBody_Returns400_AndWritesNothing(string json)
    {
        var before = backend.Gateway.Observations.Count();
        using var client = backend.Factory.ClientFor(SeedData.OidA);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/user/co-readings", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, backend.Gateway.Observations.Count());
    }

    [Fact]
    public async Task History_ReturnsOnlyCallersReadings_NewestFirst()
    {
        using var backendIsolated = new InMemoryBackend();
        using var clientA = backendIsolated.Factory.ClientFor(SeedData.OidA);
        using var clientB = backendIsolated.Factory.ClientFor(SeedData.OidB);
        var older = NewId();
        var newer = NewId();
        await clientA.PostAsJsonAsync("/user/co-readings", Reading(older, measuredAt: DateTimeOffset.UtcNow.AddDays(-2)));
        await clientA.PostAsJsonAsync("/user/co-readings", Reading(newer, measuredAt: DateTimeOffset.UtcNow.AddHours(-1)));
        await clientB.PostAsJsonAsync("/user/co-readings", Reading(NewId()));

        var history = await clientA.GetFromJsonAsync<JsonElement>($"/user/co-readings?patient={SeedData.PatientB}");

        Assert.Equal([newer, older], history.EnumerateArray().Select(r => r.GetProperty("clientReadingId").GetString()).ToList());
    }

    [Theory]
    [InlineData("from=2026-11-10T00:00:00Z&to=2026-11-01T00:00:00Z")] // from after to
    [InlineData("from=2024-01-01T00:00:00Z&to=2026-01-01T00:00:00Z")] // over 366 days
    public async Task History_BadRange_Returns400(string query)
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/user/co-readings?{query}")).StatusCode);
    }

    [Fact]
    public async Task EveryFhirCall_IsScopedToTheCaller()
    {
        using var backendIsolated = new InMemoryBackend();
        using var client = backendIsolated.Factory.ClientFor(SeedData.OidA);

        await client.PostAsJsonAsync("/user/co-readings", Reading(NewId()));
        await client.GetAsync("/user/co-readings");
        await client.PostAsJsonAsync("/user/chat-messages",
            new { clientMessageId = NewId(), text = "Synthetic message.", sentAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        var scoped = backendIsolated.Gateway.Calls.Where(c => c.Operation != "Patient?identifier").ToList();
        Assert.NotEmpty(scoped);
        Assert.All(scoped, c => Assert.Equal(SeedData.PatientA, c.PatientId));
    }
}
