# MedApp API — project context for Claude Code

## What this is
Backend API for a patient mobile app and a client-owned chatbot, built for a US healthcare provider.
The data is PHI and must be handled under HIPAA. Medications and doses are NOT currently supported (removed).
The app records exhaled breath CO from a Bedfont iCO Smokerlyzer (Android SDK) and sends patient chat messages.
This repo contains ONLY the ASP.NET Core API and local dev tooling.
There is no Azure access yet, so everything must run and be tested locally.

## Architecture (target, for context)
Mobile app → Application Gateway (WAF) → this API (Azure App Service) → Azure Health Data Services FHIR service (private endpoint).
- Patient sign-in: Microsoft Entra External ID (OIDC). The API validates JWT bearer tokens.
- API → FHIR: managed identity token in Azure; locally the same handler uses your `az login` (DefaultAzureCredential). There is no local FHIR server.
- No SQL database. All data lives in FHIR (decision: FHIR-only; add SQL only if a real need appears).
- A client-owned Foundry chatbot calls `/chatbot/*` with a delegated **on-behalf-of** token: the patient's `oid` plus the
  chatbot's client id in `azp`/`appid`, which must equal `Chatbot:ClientId`. Never app-only tokens for patient data.

## Tech stack (as built)
- .NET 10 LTS, ASP.NET Core **minimal APIs** (route groups + TypedResults). Don't add controllers.
- `Hl7.Fhir.R4` 6.x (Firely) for FHIR models and `FhirClient`
- `Microsoft.Identity.Web` for External ID. `Auth:Mode=LocalJwt` for `dotnet user-jwts`; this mode is only allowed in Development/Testing.
- `Azure.Identity` (DefaultAzureCredential), used only by `ManagedIdentityTokenHandler` when `Fhir:UseManagedIdentity=true`
- `Azure.Monitor.OpenTelemetry.AspNetCore`, enabled only when a connection string is set
- Built-in `Microsoft.AspNetCore.OpenApi`, generated only in the Testing environment and snapshotted to `docs/openapi.json` (the contract the chatbot imports). No Swagger UI; nothing is served in Development or Production.
- Tests: xUnit, WebApplicationFactory, in-memory `IFhirGateway` fake. Package versions are in `api/Directory.Packages.props`.

## Repo layout
```
/api/MedApp.sln
/api/src/MedApp.Api/          API (Auth/, Fhir/, Patients/, Users/, Chatbot/, Infrastructure/)
/api/tests/MedApp.Api.Tests/  tests (in-memory FHIR fake; synthetic data in TestData/seed-bundle.json)
/.github/workflows/pr.yml     build + test on PR
/docs/                        api-contract.md, decisions.md, openapi.json
```

## Local dev commands
```bash
az login                                   # identity used to call the Azure FHIR service
dotnet user-secrets set Fhir:Url <azure-fhir-url> --project api/src/MedApp.Api
dotnet build api
dotnet test api
dotnet run --project api/src/MedApp.Api --launch-profile https
dotnet user-jwts create --project api/src/MedApp.Api --claim oid=11111111-1111-4111-8111-111111111111   # patient token
dotnet user-jwts create --project api/src/MedApp.Api --claim oid=11111111-1111-4111-8111-111111111111 --claim azp=medapp-local-chatbot   # chatbot (OBO) token
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test api --filter "FullyQualifiedName~Contract_MatchesSnapshot"   # after an intended contract change
```

## FHIR data model
| Concept | FHIR resource | Notes |
|---|---|---|
| Patient ↔ app user link | `Patient.identifier` system `https://medapp.example/external-id`, value = user's `oid` claim | Look up with `GET Patient?identifier=<system>|<oid>` |
| iCO CO reading | `Observation`: `code` = PLACEHOLDER `https://medapp.example/codes|exhaled-co` (swap for the client's LOINC code), `valueQuantity` ppm (UCUM `[ppm]`), `effectiveDateTime`, `status` final (stable) / preliminary (unstable), `identifier` `https://medapp.example/co-reading-id|<clientReadingId>` | Created by patients via the API |
| Patient chat message | `Communication`: `subject` = `sender` = caller, `category` `https://medapp.example/codes|patient-chat-message`, `payload.contentString`, `sent`, `identifier` `https://medapp.example/chat-message-id|<clientMessageId>` | Created by patients via the API |
| Chatbot response (planned) | e.g. `Communication` with `subject` set server-side | Not decided yet |

## Endpoints
- `GET  /status`: anonymous, 200 with NO data
- `POST /user/co-readings`: record an iCO reading `{ clientReadingId, ppm, reliability, measuredAt }`. 201, or 200 if already synced
- `GET  /user/co-readings?from=&to=`: the caller's CO readings, newest first (default 30 days, max 366)
- `POST /user/chat-messages`: store a patient chat message `{ clientMessageId, text, sentAt }`. 201, or 200 if already synced. Relay to the chatbot is a STUB
- `GET  /chatbot/context`: STUB (501). Intent: chatbot requests FHIR info about the signed-in patient
- `POST /chatbot/responses`: STUB (501). Intent: chatbot submits the response it gave the signed-in patient. Body `{ text }`

## NON-NEGOTIABLE RULES (HIPAA)
1. **Check ownership on every request.** Resolve caller → FHIR Patient from the validated token's `oid` claim ONLY.
   Never trust a patient ID from the URL, query string or body. On writes, set `subject` server-side.
   On reads, always constrain FHIR searches with `subject=Patient/<callerPatientId>`.
   This applies to `/chatbot/*` too: its token is delegated (on-behalf-of), so the patient comes from its `oid`, no exceptions.
2. **No PHI in logs, traces, exceptions or error responses.** Log resource IDs and correlation IDs only.
   Never log names, birth dates, medication names, CO values, chat text, device serial numbers, or request/response bodies.
   Return generic error messages.
3. **No secrets in code or config files.** Use `dotnet user-secrets` locally and Key Vault in Azure. Never commit `appsettings.*.json` with real values.
4. **Synthetic data only.** Never use real patient data anywhere in dev or tests.
5. **Auth required by default.** The fallback authorization policy requires an authenticated user. Only `/status` is anonymous.
6. HTTPS only. No CORS (callers are the mobile app and servers only).
7. Keep FHIR access behind `IFhirGateway`, so the Azure auth handler can be swapped in via config without touching business logic.

## Out of scope for now
The mobile app itself (separate project; Android iCO SDK), Azure infrastructure/Bicep, SQL database,
push notifications, medications/doses, relaying chat messages to the chatbot, and the chatbot endpoints beyond their stubs.
