#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NAMESPACE="${HEALTHCARE_E2E_NAMESPACE:-doc-app}"

kubectl -n "${NAMESPACE}" delete job healthcare-e2e-tests --ignore-not-found
kubectl -n "${NAMESPACE}" apply -f "${ROOT}/deploy/k8s/e2e/e2e-test-job.yaml"
kubectl -n "${NAMESPACE}" wait --for=condition=complete job/healthcare-e2e-tests --timeout=3600s || {
  echo "E2E job did not complete successfully" >&2
  kubectl -n "${NAMESPACE}" logs job/healthcare-e2e-tests || true
  exit 1
}
kubectl -n "${NAMESPACE}" logs job/healthcare-e2e-tests
