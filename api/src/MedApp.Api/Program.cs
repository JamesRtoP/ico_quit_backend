using Azure.Monitor.OpenTelemetry.AspNetCore;
using MedApp.Api.Auth;
using MedApp.Api.Chatbot;
using MedApp.Api.Fhir;
using MedApp.Api.Infrastructure;
using MedApp.Api.Patients;
using MedApp.Api.Users;

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment(AuthenticationSetup.TestingEnvironment);
var isDevOrTest = builder.Environment.IsDevelopment() || isTesting;

// Never log outbound HTTP (FHIR) request URIs/headers: search URLs carry identifiers. (HIPAA rule 2)
builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllLoggers());

builder.Services.AddMedAppAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddFhirGateway(builder.Configuration);
builder.Services.AddScoped<ICallerPatientResolver, CallerPatientResolver>();
builder.Services.AddScoped<CallerPatient>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SafeExceptionHandler>();

// Azure Monitor: wired up, but only enabled when a connection string is configured.
var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? builder.Configuration["AzureMonitor:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsights))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsights);
}

// The OpenAPI document is generated only so tests can snapshot it to docs/openapi.json (the contract the
// chatbot imports). It is never served in Development or Production.
if (isTesting)
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

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/status", () => TypedResults.Ok())
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapUserEndpoints();
app.MapChatbotEndpoints();

if (isTesting)
{
    app.MapOpenApi(OpenApiSetup.DocumentPath).AllowAnonymous();
}

await app.RunAsync();

/// <summary>Entry point; public for WebApplicationFactory.</summary>
public partial class Program;
