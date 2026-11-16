# MedApp API

Backend API for a medication-adherence app. ASP.NET Core (.NET 10) minimal APIs over FHIR R4.
**Data is PHI under HIPAA. Use synthetic data only outside production.**

- Contract: [docs/api-contract.md](docs/api-contract.md) · [docs/openapi.json](docs/openapi.json)
- Decisions: [docs/decisions.md](docs/decisions.md)
- Rules for contributors (and Claude Code): [CLAUDE.md](CLAUDE.md)

## Prerequisites
- .NET SDK 10.0.100 or later (see `global.json`)
- Docker, for the local HAPI FHIR server and the HAPI integration tests

## Run locally

```bash
docker compose up -d                 # HAPI FHIR at http://localhost:8080/fhir (takes about 1 minute to start)
./scripts/seed.sh                    # or: pwsh ./scripts/seed.ps1   (synthetic patients, idempotent)
dotnet run --project api/src/MedApp.Api --launch-profile https
```

Get a token for a seeded test patient:

```bash
dotnet user-jwts create --project api/src/MedApp.Api --claim oid=11111111-1111-4111-8111-111111111111
```

| Test user | oid | FHIR Patient |
|---|---|---|
| A | `11111111-1111-4111-8111-111111111111` | `medapp-seed-a` (2 active meds, 1 stopped) |
| B | `22222222-2222-4222-8222-222222222222` | `medapp-seed-b` |
| Unlinked | `33333333-3333-4333-8333-333333333333` | none, so the API returns 403 |

```bash
curl -k https://localhost:7180/me/medications -H "Authorization: Bearer <token>"
```

Swagger UI (Development only) is at https://localhost:7180/swagger.

## Test

```bash
dotnet test api
```

- Fast tests use an in-memory FHIR fake and run anywhere.
- The HAPI tests (`Category=Integration`) start `hapiproject/hapi` with Testcontainers. If Docker is missing they skip. CI sets `MEDAPP_REQUIRE_DOCKER_TESTS=true`, so there they fail instead of skipping.

## Configuration

| Key | Purpose |
|---|---|
| `Auth:Mode` | `ExternalId` (default) or `LocalJwt` (Development/Testing only) |
| `ExternalId:*` | Entra External ID `Instance`, `TenantId`, `ClientId`, `Audience` |
| `Fhir:Url` | FHIR base URL |
| `Fhir:UseManagedIdentity` | `true` in Azure: attach a DefaultAzureCredential token to FHIR calls |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Enables Azure Monitor. Leave unset locally |

Keep secrets in `dotnet user-secrets` locally and in Key Vault in Azure. Never put them in `appsettings*.json`.
