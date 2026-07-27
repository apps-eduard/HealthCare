#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${HEALTHCARE_E2E_NAMESPACE:-doc-app}"
COMPONENT="${1:-api}"

case "${COMPONENT}" in
  api) kubectl -n "${NAMESPACE}" logs -l app.kubernetes.io/instance=healthcare-api-e2e --tail=200 ;;
  web) kubectl -n "${NAMESPACE}" logs -l app.kubernetes.io/instance=healthcare-web-e2e --tail=200 ;;
  migrate) kubectl -n "${NAMESPACE}" logs job/healthcare-db-migrate-e2e --tail=200 ;;
  prepare) kubectl -n "${NAMESPACE}" logs job/healthcare-db-prepare-e2e --tail=200 ;;
  tests) kubectl -n "${NAMESPACE}" logs job/healthcare-e2e-tests --tail=400 ;;
  *) echo "Usage: $0 [api|web|migrate|prepare|tests]" >&2; exit 1 ;;
esac
