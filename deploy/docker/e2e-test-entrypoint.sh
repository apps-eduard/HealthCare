#!/usr/bin/env bash
set -euo pipefail

: "${HEALTHCARE_E2E_API_BASE_URL:?HEALTHCARE_E2E_API_BASE_URL is required}"
: "${HEALTHCARE_E2E_WEB_BASE_URL:?HEALTHCARE_E2E_WEB_BASE_URL is required}"
: "${HEALTHCARE_E2E_CONNECTION_STRING:?HEALTHCARE_E2E_CONNECTION_STRING is required}"

RESULTS_DIR="${HEALTHCARE_E2E_RESULTS_DIR:-/artifacts/TestResults}"
mkdir -p "${RESULTS_DIR}"

echo "Running HealthCare.EndToEndTests against external API/Web…"
# Do not echo connection strings or secrets.
dotnet test tests/HealthCare.EndToEndTests/HealthCare.EndToEndTests.csproj \
  -c Release \
  --no-restore \
  --logger "trx;LogFileName=healthcare-e2e.trx" \
  --results-directory "${RESULTS_DIR}" \
  --logger "console;verbosity=normal"
