#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${HEALTHCARE_E2E_NAMESPACE:-doc-app}"

kubectl -n "${NAMESPACE}" get deploy,svc,ingress,job,pods -l app.kubernetes.io/environment=e2e -o wide
echo
kubectl -n "${NAMESPACE}" top pods -l app.kubernetes.io/environment=e2e 2>/dev/null || echo "(metrics-server not available)"
