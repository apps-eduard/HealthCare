#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NAMESPACE="${HEALTHCARE_E2E_NAMESPACE:-doc-app}"

kubectl -n "${NAMESPACE}" delete job healthcare-db-prepare-e2e --ignore-not-found
kubectl -n "${NAMESPACE}" apply -f "${ROOT}/deploy/k8s/e2e/prepare-db-job.yaml"
kubectl -n "${NAMESPACE}" wait --for=condition=complete job/healthcare-db-prepare-e2e --timeout=600s
kubectl -n "${NAMESPACE}" logs job/healthcare-db-prepare-e2e
