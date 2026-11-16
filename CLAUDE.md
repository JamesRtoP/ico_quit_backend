# MedApp API — project context for Claude Code

## What this is
Backend API for a medication-adherence mobile app built for a US healthcare provider.
Patients record when they take medication. The data is PHI and must be handled under HIPAA.
This repo contains ONLY the ASP.NET Core API and local dev tooling.
There is no Azure access yet, so everything must run and be tested locally.

## Architecture (target, for context)
Mobile app → Application Gateway (WAF) → this API (Azure App Service) → Azure Health Data Services FHIR service (private endpoint).
- Patient sign-in: Microsoft Entra External ID (OIDC). The API validates JWT bearer tokens.
- API → FHIR: managed identity token in Azure. Locally, no auth against HAPI.
- No SQL database. All data lives in FHIR (decision: FHIR-only; add SQL only if a real need appears).
- A client-owned Foundry chatbot will call this API later, so keep the endpoints and OpenAPI contract reusable by it.

## Tech stack (as built)
- .NET 10 LTS, ASP.NET Core **minimal APIs** (route groups + TypedResults). Don't add controllers.
- `Hl7.Fhir.R4` 6.x (Firely) for FHIR models and `FhirClient`
- `Microsoft.Identity.Web` for External ID. `Auth:Mode=LocalJwt` for `dotnet user-jwts`; this mode is only allowed in Development/Testing.
- `Azure.Identity` (DefaultAzureCredential), used only by `ManagedIdentityTokenHandler` when `Fhir:UseManagedIdentity=true`
- `Azure.Monitor.OpenTelemetry.AspNetCore`, enabled only when a connection string is set
- Built-in `Microsoft.AspNetCore.OpenApi` + Swagger UI (Development only). Contract snapshot is in `docs/openapi.json`.
- Tests: xUnit, WebApplicationFactory, Testcontainers (HAPI). Package versions are in `api/Directory.Packages.props`.

## Repo layout
```
/api/MedApp.sln
/api/src/MedApp.Api/          API (Auth/, Fhir/, Patients/, Medications/, Infrastructure/)
/api/tests/MedApp.Api.Tests/  tests; OwnershipScenarios run against both in-memory FHIR and HAPI
/docker-compose.yml           local HAPI FHIR server
/scripts/                     seed.sh / seed.ps1 + seed/seed-bundle.json (also used by tests)
/.github/workflows/pr.yml     build + test on PR
/docs/                        api-contract.md, decisions.md, openapi.json
```

## Local dev commands
```bash
docker compose up -d                       # HAPI FHIR at http://localhost:8080/fhir
./scripts/seed.sh                          # synthetic test patients A/B (see README for oids)
dotnet build api
dotnet test api
dotnet run --project api/src/MedApp.Api --launch-profile https
dotnet user-jwts create --project api/src/MedApp.Api --claim oid=11111111-1111-4111-8111-111111111111
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test api --filter "FullyQualifiedName~Contract_MatchesSnapshot"   # after an intended contract change
```

## FHIR data model
| Concept | FHIR resource | Notes |
|---|---|---|
| Patient ↔ app user link | `Patient.identifier` system `https://medapp.example/external-id`, value = user's `oid` claim | Look up with `GET Patient?identifier=<system>|<oid>` |
| Prescribed meds + schedule | `MedicationRequest` (`dosageInstruction.timing`) | Read-only for patients |
| Dose taken / skipped | `MedicationAdministration` (`status` = completed / not-done, `effectiveDateTime`, `subject`, `request`) | Created by patients via the API |
| Drug codes | RxNorm (`http://www.nlm.nih.gov/research/umls/rxnorm`) | |

## Endpoints
- `GET  /healthz`: anonymous, 200 with NO data
- `GET  /me/medications`: the caller's active MedicationRequests
- `POST /me/doses`: record a dose (MedicationAdministration) for the caller
- `GET  /me/doses?from=&to=`: the caller's dose history

## NON-NEGOTIABLE RULES (HIPAA)
1. **Check ownership on every request.** Resolve caller → FHIR Patient from the validated token's `oid` claim ONLY.
   Never trust a patient ID from the URL, query string or body. On writes, set `subject` server-side.
   On reads, always constrain FHIR searches with `subject=Patient/<callerPatientId>`.
2. **No PHI in logs, traces, exceptions or error responses.** Log resource IDs and correlation IDs only.
   Never log names, birth dates, medication names, or request/response bodies. Return generic error messages.
3. **No secrets in code or config files.** Use `dotnet user-secrets` locally and Key Vault in Azure. Never commit `appsettings.*.json` with real values.
4. **Synthetic data only.** Never use real patient data anywhere in dev or tests.
5. **Auth required by default.** The fallback authorization policy requires an authenticated user. Only `/healthz` is anonymous.
6. HTTPS only. No CORS (callers are the mobile app and servers only).
7. Keep FHIR access behind `IFhirGateway`, so the Azure auth handler can be swapped in via config without touching business logic.

## Out of scope for now
Mobile app (blocked until the client provides SDK access), Azure infrastructure/Bicep, SQL database,
push notifications, and chatbot integration beyond keeping the OpenAPI contract clean.
