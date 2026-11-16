#!/usr/bin/env bash
# Loads SYNTHETIC seed data into the local FHIR server (idempotent: PUTs with fixed ids).
# Usage: scripts/seed.sh [fhir-base-url]     (default http://localhost:8080/fhir)
#
# Test users (pass as oid to `dotnet user-jwts create --claim oid=<oid>`):
#   Patient A  medapp-seed-a  oid 11111111-1111-4111-8111-111111111111
#   Patient B  medapp-seed-b  oid 22222222-2222-4222-8222-222222222222
#   Unlinked   (no Patient)   oid 33333333-3333-4333-8333-333333333333  -> 403
set -euo pipefail

FHIR_URL="${1:-${FHIR_URL:-http://localhost:8080/fhir}}"
BUNDLE="$(cd "$(dirname "$0")" && pwd)/seed/seed-bundle.json"

case "$FHIR_URL" in
  http://localhost*|http://127.0.0.1*) ;;
  *) echo "Refusing to seed a non-local FHIR server: $FHIR_URL" >&2; exit 1 ;;
esac

echo "Waiting for FHIR server at $FHIR_URL ..."
for _ in $(seq 1 60); do
  if curl -fsS -o /dev/null "$FHIR_URL/metadata"; then break; fi
  sleep 3
done
curl -fsS -o /dev/null "$FHIR_URL/metadata" || { echo "FHIR server not reachable" >&2; exit 1; }

echo "Posting seed transaction bundle ..."
curl -fsS -o /dev/null -X POST "$FHIR_URL" \
  -H "Content-Type: application/fhir+json" \
  --data-binary "@$BUNDLE"
echo "Seed data loaded."
