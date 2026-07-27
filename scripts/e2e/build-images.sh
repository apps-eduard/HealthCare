#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${ROOT}"

GIT_SHA="$(git rev-parse --short=12 HEAD)"
export GIT_SHA

API_TAG="healthcare-api:e2e-${GIT_SHA}"
WEB_TAG="healthcare-web:e2e-${GIT_SHA}"
MIG_TAG="healthcare-db-migrate:e2e-${GIT_SHA}"
TEST_TAG="healthcare-e2e-tests:e2e-${GIT_SHA}"

echo "Building images for commit ${GIT_SHA}"
docker build -f deploy/docker/Dockerfile.api --build-arg "GIT_SHA=${GIT_SHA}" -t "${API_TAG}" -t healthcare-api:e2e-local .
docker build -f deploy/docker/Dockerfile.web --build-arg "GIT_SHA=${GIT_SHA}" -t "${WEB_TAG}" -t healthcare-web:e2e-local .
docker build -f deploy/docker/Dockerfile.dbmigrate --build-arg "GIT_SHA=${GIT_SHA}" -t "${MIG_TAG}" -t healthcare-db-migrate:e2e-local .
docker build -f deploy/docker/Dockerfile.e2e-tests --build-arg "GIT_SHA=${GIT_SHA}" -t "${TEST_TAG}" -t healthcare-e2e-tests:e2e-local .

echo "Built:"
echo "  ${API_TAG}"
echo "  ${WEB_TAG}"
echo "  ${MIG_TAG}"
echo "  ${TEST_TAG}"
echo "Also tagged *:e2e-local for kustomize defaults."
echo
echo "Import into k3s (on the Ubuntu node):"
echo "  docker save ${API_TAG} ${WEB_TAG} ${MIG_TAG} ${TEST_TAG} | sudo k3s ctr images import -"
