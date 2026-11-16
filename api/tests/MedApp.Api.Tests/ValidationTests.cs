using System.Net;
using System.Net.Http.Json;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

public sealed class ValidationTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    [Theory]
    [InlineData("{\"status\":\"taken\"}")]                                              // missing medicationRequestId
    [InlineData("{\"medicationRequestId\":\"medapp-seed-a-mr1\"}")]                     // missing status
    [InlineData("{\"medicationRequestId\":\"medapp-seed-a-mr1\",\"status\":\"eaten\"}")] // unknown status
    [InlineData("{\"medicationRequestId\":\"medapp-seed-a-mr1\",\"status\":0}")]         // integer enum not allowed
    [InlineData("{\"medicationRequestId\":\"bad id/../x\",\"status\":\"taken\"}")]       // not a FHIR id
    [InlineData("{\"medicationRequestId\":\"medapp-seed-a-mr1\",\"status\":\"taken\",\"takenAt\":\"2999-01-01T00:00:00Z\"}")] // future
    [InlineData("{not json")]
    public async Task RecordDose_InvalidBody_Returns400_AndWritesNothing(string json)
    {
        var before = backend.Gateway.Administrations.Count();
        using var client = backend.Factory.ClientFor(SeedData.OidA);

        var response = await client.PostAsync("/me/doses", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, backend.Gateway.Administrations.Count());
    }

    [Theory]
    [InlineData("from=2026-11-10T00:00:00Z&to=2026-11-01T00:00:00Z")] // from after to
    [InlineData("from=2025-01-01T00:00:00Z&to=2026-11-01T00:00:00Z")] // > 366 days
    [InlineData("from=yesterday")]
    public async Task Doses_InvalidRange_Returns400(string query)
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/me/doses?{query}")).StatusCode);
    }

    [Fact]
    public async Task RecordDose_UnknownMedication_Returns404()
    {
        using var client = backend.Factory.ClientFor(SeedData.OidA);

        var response = await client.PostAsJsonAsync("/me/doses", new { medicationRequestId = "does-not-exist", status = "taken" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
