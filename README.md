# MedApp API

Backend API for a medication-adherence app. ASP.NET Core (.NET 10) minimal APIs over FHIR R4.
**Data is PHI under HIPAA. Use synthetic data only outside production.**

- Contract: [docs/api-contract.md](docs/api-contract.md) · [docs/openapi.json](docs/openapi.json)
- Decisions: [docs/decisions.md](docs/decisions.md)
- Rules for contributors (and Claude Code): [CLAUDE.md](CLAUDE.md)

## Prerequisites
- .NET SDK 10.0.100 or later (see `global.json`)
- Azure CLI (`az login`), once you have access to the FHIR service

## Run locally

There is no local FHIR server. In Development the API calls the real Azure Health Data Services FHIR
service using your `az login` identity (`Fhir:UseManagedIdentity=true` → `DefaultAzureCredential`).
Your account needs a FHIR data role (e.g. FHIR Data Contributor) on the service.

```bash
az login
dotnet user-secrets set Fhir:Url https://<workspace>-<fhir>.fhir.azurehealthcareapis.com --project api/src/MedApp.Api
dotnet run --project api/src/MedApp.Api --launch-profile https
```

Until `Fhir:Url` is set, the API refuses to start (fail closed).

Get a token whose `oid` matches the `https://medapp.example/external-id` identifier of a **synthetic**
Patient in that FHIR service:

```bash
dotnet user-jwts create --project api/src/MedApp.Api --claim oid=<patient-oid>
```

```bash
curl -k https://localhost:7180/me/medications -H "Authorization: Bearer <token>"
```

A valid token with no linked Patient gets 403. Swagger UI (Development only) is at https://localhost:7180/swagger.

## Test

```bash
dotnet test api
```

- All tests run in-process against an in-memory FHIR fake (`InMemoryFhirGateway`) loaded with synthetic data from `api/tests/MedApp.Api.Tests/TestData/seed-bundle.json`. No Docker or network needed.
- Tests against the real FHIR service are a follow-up once access exists.

## Configuration

| Key | Purpose |
|---|---|
| `Auth:Mode` | `ExternalId` (default) or `LocalJwt` (Development/Testing only) |
| `ExternalId:*` | Entra External ID `Instance`, `TenantId`, `ClientId`, `Audience` |
| `Fhir:Url` | FHIR base URL |
| `Fhir:UseManagedIdentity` | `true` in Azure: attach a DefaultAzureCredential token to FHIR calls |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Enables Azure Monitor. Leave unset locally |

Keep secrets in `dotnet user-secrets` locally and in Key Vault in Azure. Never put them in `appsettings*.json`.
