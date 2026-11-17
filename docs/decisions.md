# Decisions

Short records of choices made while scaffolding. Newest last.

## D1. .NET 10 LTS (not .NET 8)
.NET 8 left support on 10 Nov 2026. The API targets `net10.0`, and `global.json` pins SDK 10.0.100 with `latestFeature` roll-forward.

## D2. Minimal APIs
Minimal APIs with route groups and `TypedResults`, so the OpenAPI response types come from the handler signatures. Don't add controllers.

## D3. Two auth modes, chosen by `Auth:Mode`
- `ExternalId` (default): `Microsoft.Identity.Web`, config section `ExternalId` (Instance, TenantId, ClientId, Audience). Startup fails if `ClientId` is missing.
- `LocalJwt`: plain JWT bearer configured from `Authentication:Schemes:Bearer`, which is what `dotnet user-jwts` writes. Startup **refuses** this mode outside Development/Testing.

The fallback authorization policy requires an authenticated user. Only `/status` opts out.

**Follow-up:** once the External ID app registration exists, require a specific scope (`scp`) on `/user`, not just a valid audience.

## D4. Caller resolution: `oid` → Patient, fail closed
An endpoint filter on the `/user` group reads `oid` from the validated token. It must be a GUID. The filter searches `Patient?identifier=https://medapp.example/external-id|<oid>`. Zero matches or more than one → `403`. The Patient id goes into a scoped `CallerPatient`. No handler takes a patient id as input.

There is no cross-request cache yet: each request does one extra FHIR search. If that cost matters later, add a short-TTL cache keyed by oid.

## D5. Ownership: always scope FHIR queries to the Patient
Every `IFhirGateway` method that touches patient data must take the resolved Patient id and add `subject=Patient/<id>` to the FHIR search. Single resources are looked up with `_id` **and** `subject`, not read by id, so the FHIR server enforces ownership, and a resource belonging to someone else looks the same as a missing one (`404`). Today the gateway only has the Patient lookup; this applies to every method added later.

## D6. PHI-safe logging and errors
- Outbound HttpClient logging is removed (`RemoveAllLoggers`), because FHIR search URLs contain identifiers.
- `SafeExceptionHandler` logs only the exception **type**, route, status and trace id. It never logs the message, because FHIR OperationOutcomes and serializer errors can echo content. The framework's own unhandled-exception log is suppressed.
- Tests capture all logs at Trace level and assert that seeded names, birth dates and user oids never appear.

## D7. No local FHIR server
The local HAPI server (docker-compose, seed scripts, Testcontainers tests) was removed. Development runs against the real Azure Health Data Services FHIR service with `az login` credentials, and automated tests use the in-memory fake. Integration tests against the real service come once access exists.

## D8. Managed identity is a config switch
`Fhir:UseManagedIdentity=true` adds `ManagedIdentityTokenHandler` (DefaultAzureCredential, scope `<fhir authority>/.default`) to the FHIR HttpClient. The handler only sends a token to the configured HTTPS FHIR host. Business logic doesn't change.

## D9. Telemetry
Azure Monitor OpenTelemetry is enabled only when `APPLICATIONINSIGHTS_CONNECTION_STRING` (or `AzureMonitor:ConnectionString`) is set. Before enabling it in Azure, check that HTTP client spans redact query strings. Recent OTel versions do this by default.

## D10. Tests run against an in-memory FHIR fake
`OwnershipTests` and the other suites run against `InMemoryFhirGateway`, loaded from `TestData/seed-bundle.json`. It mimics the FHIR searches the gateway uses and records every call, so tests can check that every FHIR call was scoped to the caller. The real `FirelyFhirGateway` is not exercised by automated tests yet.

**Follow-up:** add integration tests against the Azure FHIR service (synthetic data only) once access exists.

## D11. Medications and doses removed; `/me` → `/user`, `/healthz` → `/status`
Medications and doses are not currently supported, so their endpoints, DTOs, FHIR gateway methods and test data were removed. The patient-token group is now `/user` and has no endpoints yet; `CallerPatientFilter` is kept on it and unit-tested directly. The health check is `/status`.

## D12. Chatbot endpoints: delegated (on-behalf-of) tokens
The client's Foundry chatbot calls `/chatbot/*` **on behalf of the patient who is chatting**. The patient signs in with External ID, and the chatbot exchanges that token for a token to this API (OAuth 2.0 on-behalf-of flow). The one token carries both identities, and the API requires both:
- **The patient:** `oid`, resolved to the FHIR Patient by the same `CallerPatientFilter` as `/user`. No match → `403`.
- **The chatbot:** `azp` (v2) or `appid` (v1) must equal `Chatbot:ClientId` (policy `Chatbot`). A patient's own token, or a token issued to any other app, gets `403` before any FHIR call. If `Chatbot:ClientId` is not configured, every chatbot call is denied.

No request carries a patient id, so neither the chatbot nor its model can choose which patient is accessed. That matters with an LLM: a prompt-injected model can only ever reach the patient whose token it holds.

*Rejected:* an app-only chatbot token with the patient named by `userId` in the body. That made the chatbot trusted for every patient, so a compromised or prompt-injected chatbot could read anyone. It also needed an exception to HIPAA rule 1.

Both endpoints are **stubs**: they check auth and input, then return `501`. They read and store no patient data.
- `GET /chatbot/context`: intended to return the FHIR information the chatbot needs about the signed-in patient.
- `POST /chatbot/responses` `{ text }`: intended to store the chatbot's response (e.g. FHIR `Communication`, `subject` set server-side). `text` may contain PHI and is never logged.

**Follow-ups:**
- Confirm the chatbot signs patients in through the same External ID tenant, and that External ID supports on-behalf-of for this setup.
- Register the chatbot app and put its client id in `Chatbot:ClientId`.
- Expose a scope for the chatbot (e.g. `Chatbot.Access`) and require it in `scp`.
- Configure the Foundry tool to attach the token outside the model's context.
- Decide what `context` returns and how responses are stored.

## D13. No Swagger UI; the OpenAPI spec is a committed file
Swagger UI (and the Swashbuckle package) was removed, and the running API no longer serves `/openapi/v1.json` in Development. The OpenAPI document is still generated, but only in the Testing environment, where `Contract_MatchesSnapshot` compares it with `docs/openapi.json`. That file is the contract for the chatbot: Azure AI Foundry imports an OpenAPI spec to let an agent call an API, so it must stay accurate. Regenerate it with `UPDATE_OPENAPI_SNAPSHOT=1` after an intended contract change.
