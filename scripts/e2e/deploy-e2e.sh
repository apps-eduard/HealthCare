#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NAMESPACE="${HEALTHCARE_E2E_NAMESPACE:-doc-app}"
KUSTOMIZE_DIR="${ROOT}/deploy/k8s/e2e"

kubectl get ns "${NAMESPACE}" >/dev/null
kubectl -n "${NAMESPACE}" get secret healthcare-e2e-secrets >/dev/null

kubectl apply -k "${KUSTOMIZE_DIR}"

echo "Waiting for API rollout…"
kubectl -n "${NAMESPACE}" rollout status deployment/healthcare-api-e2e --timeout=300s
echo "Waiting for Web rollout…"
kubectl -n "${NAMESPACE}" rollout status deployment/healthcare-web-e2e --timeout=300s

kubectl -n "${NAMESPACE}" get deploy,svc,ingress -l app.kubernetes.io/environment=e2e
