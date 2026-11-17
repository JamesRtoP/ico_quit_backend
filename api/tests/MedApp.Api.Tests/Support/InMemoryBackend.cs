namespace MedApp.Api.Tests.Support;

/// <summary>The API hosted in-process against <see cref="InMemoryFhirGateway"/> loaded with synthetic Patients.</summary>
public sealed class InMemoryBackend : IDisposable
{
    public InMemoryFhirGateway Gateway { get; } = InMemoryFhirGateway.FromSeed();

    public MedAppFactory Factory { get; }

    public InMemoryBackend() => Factory = new MedAppFactory(gateway: Gateway);

    public void Dispose() => Factory.Dispose();
}
