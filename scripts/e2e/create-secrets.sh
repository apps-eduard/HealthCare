#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${HEALTHCARE_E2E_NAMESPACE:-doc-app}"

: "${HEALTHCARE_E2E_CONNECTION_STRING:?Set HEALTHCARE_E2E_CONNECTION_STRING (Database=health_care_e2e only)}"
: "${HEALTHCARE_E2E_JWT_SIGNING_KEY:?Set HEALTHCARE_E2E_JWT_SIGNING_KEY (32+ chars)}"

case "${HEALTHCARE_E2E_CONNECTION_STRING}" in
  *Database=health_care_e2e*|*database=health_care_e2e*) ;;
  *)
    echo "Refusing: connection string Database must be health_care_e2e" >&2
    exit 1
    ;;
esac

# Seed passwords default to Development fixtures when not overridden.
ADMIN_EMAIL="${HEALTHCARE_E2E_ADMIN_EMAIL:-admin@healthcare.local}"
ADMIN_PASSWORD="${HEALTHCARE_E2E_ADMIN_PASSWORD:-ChangeMe_Admin_1!}"
PATIENT_EMAIL="${HEALTHCARE_E2E_PATIENT_EMAIL:-patient@healthcare.local}"
PATIENT_PASSWORD="${HEALTHCARE_E2E_PATIENT_PASSWORD:-ChangeMe_Patient_1!}"
STAFF_EMAIL="${HEALTHCARE_E2E_STAFF_EMAIL:-doctor.a@healthcare.local}"
STAFF_PASSWORD="${HEALTHCARE_E2E_STAFF_PASSWORD:-ChangeMe_DoctorA_1!}"
STAFF_B_EMAIL="${HEALTHCARE_E2E_STAFF_B_EMAIL:-doctor.b@healthcare.local}"
STAFF_B_PASSWORD="${HEALTHCARE_E2E_STAFF_B_PASSWORD:-ChangeMe_DoctorB_1!}"
ORG_EMAIL="${HEALTHCARE_E2E_ORGADMIN_EMAIL:-orgadmin@healthcare.local}"
ORG_PASSWORD="${HEALTHCARE_E2E_ORGADMIN_PASSWORD:-ChangeMe_OrgAdmin_1!}"
CA_EMAIL="${HEALTHCARE_E2E_CLINICADMIN_EMAIL:-clinicadmin@healthcare.local}"
CA_PASSWORD="${HEALTHCARE_E2E_CLINICADMIN_PASSWORD:-ChangeMe_ClinicAdmin_1!}"

kubectl -n "${NAMESPACE}" delete secret healthcare-e2e-secrets --ignore-not-found
kubectl -n "${NAMESPACE}" create secret generic healthcare-e2e-secrets \
  --from-literal="ConnectionStrings__DefaultConnection=${HEALTHCARE_E2E_CONNECTION_STRING}" \
  --from-literal="Jwt__SigningKey=${HEALTHCARE_E2E_JWT_SIGNING_KEY}" \
  --from-literal="DevelopmentSeed__Admin__Email=${ADMIN_EMAIL}" \
  --from-literal="DevelopmentSeed__Admin__Password=${ADMIN_PASSWORD}" \
  --from-literal="DevelopmentSeed__Patient__Email=${PATIENT_EMAIL}" \
  --from-literal="DevelopmentSeed__Patient__Password=${PATIENT_PASSWORD}" \
  --from-literal="DevelopmentSeed__Patient__StaffEmail=${STAFF_EMAIL}" \
  --from-literal="DevelopmentSeed__Patient__StaffPassword=${STAFF_PASSWORD}" \
  --from-literal="DevelopmentSeed__Patient__OtherClinicStaffEmail=${STAFF_B_EMAIL}" \
  --from-literal="DevelopmentSeed__Patient__OtherClinicStaffPassword=${STAFF_B_PASSWORD}" \
  --from-literal="DevelopmentSeed__Patient__OrganizationAdminEmail=${ORG_EMAIL}" \
  --from-literal="DevelopmentSeed__Patient__OrganizationAdminPassword=${ORG_PASSWORD}" \
  --from-literal="DevelopmentSeed__Patient__ClinicAdminEmail=${CA_EMAIL}" \
  --from-literal="DevelopmentSeed__Patient__ClinicAdminPassword=${CA_PASSWORD}" \
  --dry-run=client -o yaml | kubectl apply -f -

echo "Secret healthcare-e2e-secrets applied in namespace ${NAMESPACE} (values not printed)."
