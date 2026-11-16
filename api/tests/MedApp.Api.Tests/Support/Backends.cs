using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;

namespace MedApp.Api.Tests.Support;

/// <summary>A FHIR backend the API runs against in tests, plus direct FHIR access for assertions.</summary>
public interface ITestBackend
{
    MedAppFactory Factory { get; }

    /// <summary>Skips (or, in CI, fails) the test when the backend can't run here.</summary>
    void EnsureAvailable();

    /// <summary>Total MedicationAdministrations for the patient, read straight from FHIR.</summary>
    Task<int> CountAdministrationsAsync(string patientId);

    /// <summary>Reads a MedicationAdministration straight from FHIR.</summary>
    Task<MedicationAdministration?> ReadAdministrationAsync(string id);
}

/// <summary>Fast backend: <see cref="InMemoryFhirGateway"/> loaded with the seed bundle. Runs anywhere.</summary>
public sealed class InMemoryBackend : ITestBackend, IDisposable
{
    public InMemoryFhirGateway Gateway { get; } = InMemoryFhirGateway.FromSeed();

    public MedAppFactory Factory { get; }

    public InMemoryBackend() => Factory = new MedAppFactory(gateway: Gateway);

    public void EnsureAvailable()
    {
    }

    public Task<int> CountAdministrationsAsync(string patientId) =>
        System.Threading.Tasks.Task.FromResult(Gateway.Administrations.Count(a => a.Subject?.Reference == $"Patient/{patientId}"));

    public Task<MedicationAdministration?> ReadAdministrationAsync(string id) =>
        System.Threading.Tasks.Task.FromResult(Gateway.Administrations.FirstOrDefault(a => a.Id == id));

    public void Dispose() => Factory.Dispose();
}

/// <summary>
/// Real backend: HAPI FHIR in Docker via Testcontainers, seeded with the same bundle, API using the
/// real Firely gateway. Skipped when Docker is unavailable unless MEDAPP_REQUIRE_DOCKER_TESTS=true (CI).
/// </summary>
public sealed class HapiBackend : ITestBackend, IAsyncLifetime
{
    private static readonly bool RequireDocker =
        string.Equals(Environment.GetEnvironmentVariable("MEDAPP_REQUIRE_DOCKER_TESTS"), "true", StringComparison.OrdinalIgnoreCase);

    private static readonly string Image =
        Environment.GetEnvironmentVariable("MEDAPP_HAPI_IMAGE") is { Length: > 0 } image ? image : "hapiproject/hapi:latest";

    private IContainer? _container;
    private MedAppFactory? _factory;
    private HttpClient? _fhirHttp;
    private string? _unavailableReason;

    public MedAppFactory Factory => _factory ?? throw new InvalidOperationException("HAPI backend not started.");

    public string FhirUrl { get; private set; } = "";

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithPortBinding(8080, true)
                .WithEnvironment("hapi.fhir.fhir_version", "R4")
                .WithEnvironment("hapi.fhir.client_id_strategy", "ALPHANUMERIC")
                .WithEnvironment("hapi.fhir.reuse_cached_search_results_millis", "0")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/fhir/metadata"),
                        o => o.WithTimeout(TimeSpan.FromMinutes(4))))
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex) when (!RequireDocker)
        {
            _unavailableReason = $"Docker/HAPI unavailable ({ex.GetType().Name}); HAPI integration tests skipped. Set MEDAPP_REQUIRE_DOCKER_TESTS=true to fail instead.";
            return;
        }

        FhirUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/fhir";
        _fhirHttp = new HttpClient();
        _fhirHttp.DefaultRequestHeaders.CacheControl = new() { NoCache = true };

        using var seed = new StringContent(await File.ReadAllTextAsync(SeedData.BundlePath), System.Text.Encoding.UTF8, "application/fhir+json");
        using var response = await _fhirHttp.PostAsync(FhirUrl, seed);
        response.EnsureSuccessStatusCode();

        _factory = new MedAppFactory(fhirUrl: FhirUrl);
    }

    public void EnsureAvailable() => Skip.If(_unavailableReason is not null, _unavailableReason);

    public async Task<int> CountAdministrationsAsync(string patientId)
    {
        var json = await _fhirHttp!.GetStringAsync($"{FhirUrl}/MedicationAdministration?subject=Patient/{patientId}&_summary=count");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("total").GetInt32();
    }

    public async Task<MedicationAdministration?> ReadAdministrationAsync(string id)
    {
        using var client = new FhirClient(FhirUrl, _fhirHttp!, new FhirClientSettings { PreferredFormat = ResourceFormat.Json, VerifyFhirVersion = false });
        return await client.ReadAsync<MedicationAdministration>($"MedicationAdministration/{id}");
    }

    public async System.Threading.Tasks.Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        _fhirHttp?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
