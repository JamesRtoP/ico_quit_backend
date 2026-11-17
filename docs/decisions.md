# Decisions

Short records of choices made while scaffolding. Newest last.

## D1. .NET 10 LTS (not .NET 8)
.NET 8 left support on 10 Nov 2026. The API targets `net10.0`, and `global.json` pins SDK 10.0.100 with `latestFeature` roll-forward.

## D2. Minimal APIs
Minimal APIs with route groups and `TypedResults`, so the OpenAPI response types come from the handler signatures. Don't add controllers.

## D3. Two auth modes, chosen by `Auth:Mode`
- `ExternalId` (default): `Microsoft.Identity.Web`, config section `ExternalId` (Instance, TenantId, ClientId, Audience). Startup fails if `ClientId` is missing.
- `LocalJwt`: plain JWT bearer configured from `Authentication:Schemes:Bearer`, which is what `dotnet user-jwts` writes. Startup **refuses** this mode outside Development/Testing.

The fallback authorization policy requires an authenticated user. Only `/healthz` opts out, plus the OpenAPI/Swagger endpoints in Development.

**Follow-up:** once the External ID app registration exists, require a specific scope (`scp`) on `/me`, not just a valid audience.

## D4. Caller resolution: `oid` → Patient, fail closed
An endpoint filter on the `/me` group reads `oid` from the validated token. It must be a GUID. The filter searches `Patient?identifier=https://medapp.example/external-id|<oid>`. Zero matches or more than one → `403`. The Patient id goes into a scoped `CallerPatient`. No handler takes a patient id as input.

There is no cross-request cache yet: each request does one extra FHIR search. If that cost matters later, add a short-TTL cache keyed by oid.

## D5. Ownership enforced twice
1. Every `IFhirGateway` patient-data method takes the caller's Patient id and adds `subject=Patient/<id>` to the FHIR search. Single MedicationRequests are looked up with `_id` **and** `subject`, not read by id, so the FHIR server enforces ownership.
2. The endpoints drop any returned resource whose `subject` isn't the caller and log an error with counts only.

A MedicationRequest that belongs to someone else returns `404`, the same as a missing one. That way callers can't probe which ids exist.

## D6. PHI-safe logging and errors
- Outbound HttpClient logging is removed (`RemoveAllLoggers`), because FHIR search URLs contain identifiers.
- `SafeExceptionHandler` logs only the exception **type**, route, status and trace id. It never logs the message, because FHIR OperationOutcomes and serializer errors can echo content. The framework's own unhandled-exception log is suppressed.
- Tests capture all logs at Trace level and assert that seeded names, birth dates, medication names and user oids never appear.

## D7. No local FHIR server
The local HAPI server (docker-compose, seed scripts, Testcontainers tests) was removed. Development runs against the real Azure Health Data Services FHIR service with `az login` credentials, and automated tests use the in-memory fake. Integration tests against the real service come once access exists.

## D8. Managed identity is a config switch
`Fhir:UseManagedIdentity=true` adds `ManagedIdentityTokenHandler` (DefaultAzureCredential, scope `<fhir authority>/.default`) to the FHIR HttpClient. The handler only sends a token to the configured HTTPS FHIR host. Business logic doesn't change.

## D9. Telemetry
Azure Monitor OpenTelemetry is enabled only when `APPLICATIONINSIGHTS_CONNECTION_STRING` (or `AzureMonitor:ConnectionString`) is set. Before enabling it in Azure, check that HTTP client spans redact query strings. Recent OTel versions do this by default.

## D10. Tests run against an in-memory FHIR fake
`OwnershipTests` and the other suites run against `InMemoryFhirGateway`, loaded from `TestData/seed-bundle.json`. It mimics the FHIR searches the gateway uses and records every call, so tests can check that every FHIR call was scoped to the caller. The real `FirelyFhirGateway` is not exercised by automated tests yet.

**Follow-up:** add integration tests against the Azure FHIR service (synthetic data only) once access exists.
