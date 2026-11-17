using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedApp.Api.Tests.Support;

namespace MedApp.Api.Tests;

/// <summary>
/// HIPAA acceptance criteria, run against the in-memory FHIR fake:
/// patients see and write only their own data; client-supplied patient ids are ignored;
/// logs never contain seeded names or medication names.
/// </summary>
public sealed class OwnershipTests(InMemoryBackend backend) : IClassFixture<InMemoryBackend>
{
    private static readonly DateTimeOffset SeedWindowFrom = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeedWindowTo = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string SeedWindowQuery =
        $"from={Uri.EscapeDataString(SeedWindowFrom.ToString("O"))}&to={Uri.EscapeDataString(SeedWindowTo.ToString("O"))}";

    private MedAppFactory Factory => backend.Factory;

    [Fact]
    public async Task Medications_ReturnsOnlyCallersActivePrescriptions()
    {
        using var client = Factory.ClientFor(SeedData.OidA);

        var meds = await client.GetFromJsonAsync<JsonElement>("/me/medications");

        var ids = meds.EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToList();
        Assert.Equal([SeedData.A_Lisinopril, SeedData.A_Metformin], ids.Order().ToList());
        Assert.DoesNotContain(SeedData.B_Sertraline, ids);
        Assert.DoesNotContain(SeedData.A_AtorvastatinStopped, ids);
    }

    [Fact]
    public async Task Medications_IgnoresPatientIdInQueryString()
    {
        using var client = Factory.ClientFor(SeedData.OidA);

        var meds = await client.GetFromJsonAsync<JsonElement>(
            $"/me/medications?patient={SeedData.PatientB}&subject=Patient/{SeedData.PatientB}&patientId={SeedData.PatientB}");

        var ids = meds.EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(SeedData.B_Sertraline, ids);
        Assert.Contains(SeedData.A_Lisinopril, ids);
    }

    [Fact]
    public async Task Doses_ReturnsOnlyCallersHistory_EvenWithForeignPatientInQuery()
    {
        using var clientB = Factory.ClientFor(SeedData.OidB);

        var doses = await clientB.GetFromJsonAsync<JsonElement>($"/me/doses?{SeedWindowQuery}&patient={SeedData.PatientA}");

        var ids = doses.EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToList();
        Assert.Equal([SeedData.B_Dose1], ids);
    }

    [Fact]
    public async Task Doses_MapsSeededHistoryForCaller()
    {
        using var client = Factory.ClientFor(SeedData.OidA);

        var doses = await client.GetFromJsonAsync<JsonElement>($"/me/doses?{SeedWindowQuery}");

        var list = doses.EnumerateArray().ToList();
        Assert.Equal([SeedData.A_Dose2, SeedData.A_Dose1], list.Select(d => d.GetProperty("id").GetString()).ToList()); // newest first
        Assert.Equal("skipped", list[0].GetProperty("status").GetString());
        Assert.Equal(SeedData.A_Metformin, list[0].GetProperty("medicationRequestId").GetString());
        Assert.Equal("taken", list[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task RecordDose_AgainstAnotherPatientsMedication_Returns404_AndWritesNothing()
    {
        var beforeA = backend.CountAdministrations(SeedData.PatientA);
        var beforeB = backend.CountAdministrations(SeedData.PatientB);
        using var clientA = Factory.ClientFor(SeedData.OidA);

        var response = await clientA.PostAsJsonAsync("/me/doses", new
        {
            medicationRequestId = SeedData.B_Sertraline,
            status = "taken",
            subject = new { reference = $"Patient/{SeedData.PatientB}" },
            patientId = SeedData.PatientB,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(beforeA, backend.CountAdministrations(SeedData.PatientA));
        Assert.Equal(beforeB, backend.CountAdministrations(SeedData.PatientB));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sertraline", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordDose_SubjectAndPatientIdInBody_AreIgnored_SubjectIsCaller()
    {
        var beforeB = backend.CountAdministrations(SeedData.PatientB);
        using var clientA = Factory.ClientFor(SeedData.OidA);

        var response = await clientA.PostAsJsonAsync("/me/doses", new
        {
            medicationRequestId = SeedData.A_Lisinopril,
            status = "taken",
            takenAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            subject = new { reference = $"Patient/{SeedData.PatientB}" },
            patient = $"Patient/{SeedData.PatientB}",
            patientId = SeedData.PatientB,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal(SeedData.A_Lisinopril, created.GetProperty("medicationRequestId").GetString());

        var stored = backend.ReadAdministration(id);
        Assert.NotNull(stored);
        Assert.Equal($"Patient/{SeedData.PatientA}", stored.Subject?.Reference);
        Assert.Equal($"MedicationRequest/{SeedData.A_Lisinopril}", stored.Request?.Reference);
        Assert.Equal(beforeB, backend.CountAdministrations(SeedData.PatientB));
    }

    [Fact]
    public async Task RecordDose_ThenHistory_ShowsNewDoseImmediately()
    {
        using var client = Factory.ClientFor(SeedData.OidA);
        var takenAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        var post = await client.PostAsJsonAsync("/me/doses", new { medicationRequestId = SeedData.A_Metformin, status = "skipped", takenAt });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var doses = await client.GetFromJsonAsync<JsonElement>("/me/doses");
        Assert.Contains(id, doses.EnumerateArray().Select(d => d.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task RecordDose_AgainstStoppedMedication_Returns409()
    {
        using var client = Factory.ClientFor(SeedData.OidA);

        var response = await client.PostAsJsonAsync("/me/doses", new { medicationRequestId = SeedData.A_AtorvastatinStopped, status = "taken" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(SeedData.OidUnlinked)]
    [InlineData(null)] // token without an oid claim
    public async Task CallerWithoutLinkedPatient_Gets403_OnEveryMeEndpoint(string? oid)
    {
        using var client = Factory.ClientFor(oid);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/me/medications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/me/doses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/me/doses", new { medicationRequestId = SeedData.A_Lisinopril, status = "taken" })).StatusCode);
    }

    [Fact]
    public async Task Logs_NeverContainSeededNamesOrMedications()
    {
        using var clientA = Factory.ClientFor(SeedData.OidA);
        using var clientB = Factory.ClientFor(SeedData.OidB);
        using var unlinked = Factory.ClientFor(SeedData.OidUnlinked);

        // Exercise success, not-found, conflict, validation, forbidden and unauthenticated paths.
        await clientA.GetAsync("/me/medications");
        await clientA.GetAsync($"/me/doses?{SeedWindowQuery}");
        await clientB.GetAsync("/me/medications");
        await clientA.PostAsJsonAsync("/me/doses", new { medicationRequestId = SeedData.A_Lisinopril, status = "taken" });
        await clientA.PostAsJsonAsync("/me/doses", new { medicationRequestId = SeedData.B_Sertraline, status = "taken" });
        await clientA.PostAsJsonAsync("/me/doses", new { medicationRequestId = SeedData.A_AtorvastatinStopped, status = "taken" });
        await clientA.PostAsJsonAsync("/me/doses", new { medicationRequestId = "Lisinopril 10 MG", status = "nonsense" });
        await clientA.PostAsync("/me/doses", new StringContent("{\"medicationRequestId\": \"Metformin\"", System.Text.Encoding.UTF8, "application/json"));
        await unlinked.GetAsync("/me/medications");
        using var anonymous = Factory.Client();
        await anonymous.GetAsync("/me/doses");

        AssertNoSensitiveStrings(Factory.Logs);
    }

    internal static void AssertNoSensitiveStrings(CapturingLoggerProvider logs)
    {
        var text = logs.AllText;
        Assert.NotEmpty(logs.Lines); // the capture actually worked
        foreach (var sensitive in SeedData.SensitiveStrings)
        {
            Assert.False(text.Contains(sensitive, StringComparison.OrdinalIgnoreCase),
                $"Log output contains sensitive seed value '{sensitive}'.");
        }
    }

    [Fact]
    public async Task EveryPatientDataQuery_IsScopedToTheCaller()
    {
        backend.Gateway.Calls.Clear();
        using var client = backend.Factory.ClientFor(SeedData.OidA);

        await client.GetAsync("/me/medications");
        await client.GetAsync("/me/doses");
        await client.PostAsJsonAsync("/me/doses", new { medicationRequestId = SeedData.B_Sertraline, status = "taken", patientId = SeedData.PatientB });

        var scoped = backend.Gateway.Calls.Where(c => c.Operation != "Patient?identifier").ToList();
        Assert.NotEmpty(scoped);
        Assert.All(scoped, c => Assert.Equal(SeedData.PatientA, c.PatientId));
    }

    [Fact]
    public async Task FhirFailure_ReturnsGenericError_WithoutLeakingExceptionText()
    {
        using var backendWithFault = new InMemoryBackend();
        backendWithFault.Gateway.Fault = () => new InvalidOperationException("Lisinopril for Avery Quillfeather born 1971-03-14");
        using var client = backendWithFault.Factory.ClientFor(SeedData.OidA);

        var response = await client.GetAsync("/me/medications");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Lisinopril", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quillfeather", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traceId", body);
        AssertNoSensitiveStrings(backendWithFault.Factory.Logs);
        Assert.Contains(backendWithFault.Factory.Logs.Lines, l => l.Contains("InvalidOperationException") && l.Contains("/me/medications"));
    }

    [Fact]
    public async Task UpstreamUnavailable_Returns503()
    {
        using var backendWithFault = new InMemoryBackend();
        backendWithFault.Gateway.Fault = () => new HttpRequestException("connection refused");
        using var client = backendWithFault.Factory.ClientFor(SeedData.OidA);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/me/doses")).StatusCode);
    }
}
