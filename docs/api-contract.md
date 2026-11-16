# MedApp API contract

Machine-readable contract: [`openapi.json`](openapi.json) (OpenAPI 3.1). A test snapshots it, so any contract change shows up in the PR diff. To regenerate after an intended change:

```bash
UPDATE_OPENAPI_SNAPSHOT=1 dotnet test api --filter "FullyQualifiedName~Contract_MatchesSnapshot"
```

When the API runs locally in Development, the live document is at `/openapi/v1.json` and Swagger UI at `/swagger`. Neither is served outside Development/Testing.

## Conventions (for the mobile app and the chatbot)

- **Auth:** every endpoint except `/healthz` needs `Authorization: Bearer <access token>` from Microsoft Entra External ID. The API uses the token's `oid` claim to find the caller's FHIR Patient.
- **No patient ids from clients.** `/me/*` always means the caller. Any `patientId`, `subject` or `patient` sent in a query string or body is ignored.
- **JSON:** camelCase. Enums are lower-case strings (`"taken"`, `"skipped"`). Timestamps are ISO 8601 with an offset.
- **Errors:** [RFC 9457 problem details](https://www.rfc-editor.org/rfc/rfc9457) with a generic `title` and a `traceId`. Error responses never echo request data or medication details.

| Status | Meaning |
|---|---|
| 400 | Validation failed (`errors` object lists the field names) |
| 401 | Token missing, expired or invalid |
| 403 | Token is valid but the user isn't linked to exactly one Patient |
| 404 | The MedicationRequest doesn't exist **or isn't the caller's** (indistinguishable on purpose) |
| 409 | The MedicationRequest isn't active |
| 502 / 503 | FHIR backend error / unavailable. Safe to retry 503 |

## Endpoints

### `GET /healthz`
Anonymous. `200` with an empty body. Returns no data.

### `GET /me/medications`
The caller's **active** prescriptions (FHIR `MedicationRequest`, `status=active`).

```json
[
  {
    "id": "medapp-seed-a-mr1",
    "name": "Lisinopril 10 MG Oral Tablet",
    "code": { "system": "http://www.nlm.nih.gov/research/umls/rxnorm", "code": "314076", "display": "Lisinopril 10 MG Oral Tablet" },
    "status": "active",
    "dosageText": "Take 1 tablet by mouth once daily in the morning",
    "schedule": [ { "frequency": 1, "period": 1, "periodUnit": "d", "timeOfDay": ["08:00:00"], "text": null } ],
    "authoredOn": "2026-10-01T00:00:00+00:00"
  }
]
```

### `POST /me/doses`
Records a dose taken or skipped (stored as FHIR `MedicationAdministration`; `completed` or `not-done`).

```json
{ "medicationRequestId": "medapp-seed-a-mr1", "status": "taken", "takenAt": "2026-11-16T08:03:00Z" }
```

- `medicationRequestId` (required): an `id` from `GET /me/medications`.
- `status` (required): `taken` or `skipped`.
- `takenAt` (optional): defaults to now. Can't be more than 5 minutes in the future.

`201` returns the created dose: `{ "id": "...", "medicationRequestId": "...", "status": "taken", "takenAt": "..." }`.

### `GET /me/doses?from=&to=`
The caller's dose history, newest first. `to` defaults to now and `from` defaults to 30 days before `to`. The range can be at most 366 days. Results are capped at 1000 per request.
