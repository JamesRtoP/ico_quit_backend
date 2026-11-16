using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MedApp.Api.Auth;
using MedApp.Api.Fhir;
using MedApp.Api.Infrastructure;
using MedApp.Api.Medications;
using MedApp.Api.Patients;

var builder = WebApplication.CreateBuilder(args);
var isDevOrTest = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment(AuthenticationSetup.TestingEnvironment);

// Never log outbound HTTP (FHIR) request URIs/headers: search URLs carry identifiers. (HIPAA rule 2)
builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllLoggers());

builder.Services.AddMedAppAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddFhirGateway(builder.Configuration);
builder.Services.AddScoped<ICallerPatientResolver, CallerPatientResolver>();
builder.Services.AddScoped<CallerPatient>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SafeExceptionHandler>();

// Azure Monitor: wired up, but only enabled when a connection string is configured.
var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? builder.Configuration["AzureMonitor:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsights))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsights);
}

if (isDevOrTest)
{
    builder.Services.AddMedAppOpenApi();
}

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Our handler logs a PHI-safe line; suppress the framework's own exception log (which includes the message).
    SuppressDiagnosticsCallback = _ => true,
});
app.UseStatusCodePages();

if (!isDevOrTest)
{
    app.UseHsts();
}
app.UseHttpsRedirection();
// No CORS by design: callers are the mobile app and server-side clients only (rule 6).

if (isDevOrTest)
{
    // Static UI middleware; must run before the fallback auth policy. The document itself is mapped below.
    app.UseSwaggerUI(o => o.SwaggerEndpoint(OpenApiSetup.DocumentPath, "MedApp API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => TypedResults.Ok())
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapMeEndpoints();

if (isDevOrTest)
{
    app.MapOpenApi(OpenApiSetup.DocumentPath).AllowAnonymous();
}

await app.RunAsync();

/// <summary>Entry point; public for WebApplicationFactory.</summary>
public partial class Program;
