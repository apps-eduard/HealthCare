#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${HEALTHCARE_E2E_NAMESPACE:-doc-app}"

echo "Removing HealthCare E2E workloads in ${NAMESPACE} (PostgreSQL and namespace are kept)…"
kubectl -n "${NAMESPACE}" delete \
  deploy,svc,ingress,job \
  -l app.kubernetes.io/part-of=healthcare,app.kubernetes.io/environment=e2e \
  --ignore-not-found

kubectl -n "${NAMESPACE}" delete configmap healthcare-e2e-config --ignore-not-found
kubectl -n "${NAMESPACE}" delete secret healthcare-e2e-secrets --ignore-not-found

echo "Done. Databases health_care_dev / health_care_e2e / health_care_staging were not dropped."
