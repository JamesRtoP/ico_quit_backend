using System.Security.Claims;
using MedApp.Api.Fhir;
using MedApp.Api.Patients;
using MedApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace MedApp.Api.Tests;

/// <summary>
/// HIPAA rule 1: the /user group filter maps the token's oid to exactly one FHIR Patient, or denies with 403.
/// Tested directly because the /user group has no endpoints yet.
/// </summary>
public sealed class CallerPatientFilterTests
{
    private const string Next = "next called";

    [Fact]
    public async Task LinkedOid_ResolvesCallerPatient_AndContinues()
    {
        var (result, services) = await RunFilter(new Claim("oid", SeedData.OidA));

        Assert.Equal(Next, result);
        Assert.Equal(SeedData.PatientA, services.GetRequiredService<CallerPatient>().PatientId);
    }

    [Theory]
    [InlineData(SeedData.OidUnlinked)] // valid oid, no linked Patient
    [InlineData("not-a-guid")]
    public async Task UnlinkedOrInvalidOid_Returns403(string oid)
    {
        var (result, services) = await RunFilter(new Claim("oid", oid));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<CallerPatient>().PatientId);
    }

    [Fact]
    public async Task MissingOid_Returns403()
    {
        var (result, _) = await RunFilter();

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    private static async Task<(object? Result, IServiceProvider Services)> RunFilter(params Claim[] claims)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IFhirGateway>(InMemoryFhirGateway.FromSeed())
            .AddScoped<ICallerPatientResolver, CallerPatientResolver>()
            .AddScoped<CallerPatient>()
            .BuildServiceProvider()
            .CreateScope()
            .ServiceProvider;
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };

        var result = await new CallerPatientFilter().InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http), _ => ValueTask.FromResult<object?>(Next));
        return (result, services);
    }
}
