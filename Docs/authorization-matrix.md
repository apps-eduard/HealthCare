# Authorization matrix (MVP)

Code-defined permission catalog and role mappings. Custom DB-editable roles are deferred. No permission tables / migration.

## Persistence decision

Permissions live in:

- `HealthCare.Application.Authorization.Permissions`
- `HealthCare.Application.Authorization.RolePermissionMatrix`

Resolution uses server-side Identity roles (DB) + active staff membership + patient linkage. Client-supplied permission claims are never trusted.

## Permission catalog

| Permission | Purpose |
|------------|---------|
| `patients.read` | Read patient profile (self or staff tenant scope) |
| `patients.search` | Staff patient search |
| `patients.update_clinic_status` | Clinic patient profile / enroll |
| `patients.update_own_profile` | Patient self profile |
| `appointments.*` | Create/read/confirm/cancel/check_in/complete/no_show/reschedule |
| `availability.read` | Clinic doctors + slots |
| `availability.manage_self` | Doctor own availability |
| `availability.manage_clinic` | Clinic admin availability |
| `availability.manage_organization` | Org admin availability |
| `reminders.read` / `reminders.retry` | Staff reminder inspection |
| `summaries.read` / `summaries.retry` | Daily clinic summary |
| `clinics.read` / `clinics.manage` | Clinic discovery / management foundation |
| `staff.read` / `staff.manage` | Future staff APIs |
| `roles.read` / `roles.assign` | Future role assignment |
| `hangfire.dashboard` | Hangfire dashboard (with PLATFORM_ADMIN) |

## Role mappings (assumptions)

- **PLATFORM_ADMIN:** broad permissions including dashboard; **does not** auto-bypass tenants — requires `PlatformAdminBypass.Explicit`.
- **ORGANIZATION_ADMIN:** org-scoped ops; can assign roles except PLATFORM_ADMIN.
- **CLINIC_ADMIN:** clinic-scoped ops; cannot assign ORG/PLATFORM admin.
- **DOCTOR:** clinic appointments + own availability; no clinic administration.
- **NURSE:** clinical-operational appointment actions (no create/reschedule/availability manage; no medical-notes permissions yet).
- **RECEPTIONIST:** scheduling + search + confirm/cancel/check-in/reschedule; **no** complete/no-show; **no** availability admin.
- **PATIENT:** own profile/appointments + availability read + clinic register; no staff ops.

## Tenant / resource rules

Permission grants operation capability only. Resource access still requires:

- Active membership / linked patient
- Clinic or organization match via `ITenantAccessService`
- Patient self-scope for PATIENT actors
- Appointment state rules in services

## Explicit platform bypass

Cross-tenant reads/mutations require `platformAdminBypass=true` **and** PLATFORM_ADMIN. Audited via `IAuthorizationAuditLogger`.

## Role-assignment safety (no public endpoint yet)

`IRoleAssignmentAuthorizationService`:

- No self-elevation
- CLINIC_ADMIN cannot grant ORG/PLATFORM admin
- ORGANIZATION_ADMIN cannot grant PLATFORM_ADMIN
- Tenant scope required
- PATIENT must not mix with staff membership
- Unknown roles rejected

## Approved public endpoints

- `GET /health`, `GET /health/ready`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/register/patient`
- `POST /api/v1/auth/confirm-email`
- `POST /api/v1/auth/resend-confirmation`
- `GET /api/v1/auth/dev/confirmation-token` (Development only; 404 otherwise)
- OpenAPI/Swagger UI (Development only)

Doctor directory and available-slots require authentication + `availability.read` (not anonymous).

## Securing new endpoints

1. Add/choose a permission constant.
2. Map it in `RolePermissionMatrix` for justified roles only.
3. Annotate with `[AuthorizePermission(...)]` (and `StaffUser` / `PatientSelfScope` when needed).
4. Keep tenant/ownership checks in application services.
5. Add matrix + integration negative tests.
