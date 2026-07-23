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
| `staff.read` / `staff.manage` | Staff-management list/detail/create/update/activate |
| `roles.read` / `roles.assign` | Assignable-role catalog and role assignment |
| `hangfire.dashboard` | Hangfire dashboard (with PLATFORM_ADMIN) |

## Role mappings (assumptions)

- **PLATFORM_ADMIN:** broad permissions including dashboard; **does not** auto-bypass tenants — requires `PlatformAdminBypass.Explicit`.
- **ORGANIZATION_ADMIN:** org-scoped ops; can assign roles except PLATFORM_ADMIN.
- **CLINIC_ADMIN:** clinic-scoped ops; cannot assign ORG/PLATFORM admin.
- **DOCTOR:** clinic appointments + own availability; no clinic administration.
- **NURSE:** clinical-operational appointment actions (no create/reschedule/availability manage; no medical-notes permissions yet).
- **RECEPTIONIST:** scheduling + search + confirm/cancel/check-in/reschedule; **no** complete/no-show; **no** availability admin.
- **PATIENT:** own profile/appointments + availability read + clinic register; no staff ops.

`staff.read` / `staff.manage` / `roles.read` / `roles.assign` are granted to **PLATFORM_ADMIN**, **ORGANIZATION_ADMIN**, and **CLINIC_ADMIN** only (not DOCTOR/NURSE/RECEPTIONIST/PATIENT).

## Tenant / resource rules

Permission grants operation capability only. Resource access still requires:

- Active membership / linked patient
- Clinic or organization match via `ITenantAccessService`
- Patient self-scope for PATIENT actors
- Appointment state rules in services

## Explicit platform bypass

Cross-tenant reads/mutations require `platformAdminBypass=true` **and** PLATFORM_ADMIN. Audited via `IAuthorizationAuditLogger`.

## Staff administration APIs

Route prefix: `/api/v1/staff-management`

| Method | Path | Permission | Notes |
|--------|------|------------|-------|
| GET | `/staff` | `staff.read` | Paginated search; clinic/org scoped |
| GET | `/staff/{staffMemberId}` | `staff.read` | Detail; out-of-scope → safe 404 |
| POST | `/staff` | `staff.manage` | Temporary-password create (transactional) |
| PATCH | `/staff/{staffMemberId}` | `staff.manage` | Profile fields + optimistic concurrency |
| POST | `/staff/{id}/activate` | `staff.manage` | Reactivates membership; revokes sessions |
| POST | `/staff/{id}/deactivate` | `staff.manage` | Deactivates; revokes sessions |
| GET | `/roles` | `roles.read` | Roles caller may view/assign |
| POST | `/staff/{id}/roles/{roleName}` | `roles.assign` | Hierarchy-checked assignment |
| DELETE | `/staff/{id}/roles/{roleName}` | `roles.assign` | Hierarchy-checked removal |

Require `Authenticated` plus permission attributes. Active staff membership (or PLATFORM_ADMIN with explicit bypass) is enforced in `StaffManagementService` — not by role-name strings in the controller. `StaffUser` alone would block platform admins who have no membership.

### Tenant scoping

| Actor | List / read / manage |
|-------|----------------------|
| CLINIC_ADMIN | Own clinic only; create uses trusted ClinicId |
| ORGANIZATION_ADMIN | Own organization; optional in-org `ClinicId` filter/create |
| PLATFORM_ADMIN | Cross-tenant only with `platformAdminBypass=true` **and** a target `ClinicId` |
| PATIENT / other staff without permissions | 403 |

MVP: one active `StaffMember` per user (`UserId` unique). Operations always target an explicit `staffMemberId`.

### Creation workflow

Temporary-password mode (no real email provider in this phase):

1. Validate clinic/org active + assignable role via `IRoleAssignmentAuthorizationService`
2. Create Identity user + staff membership + Identity role in one transaction
3. On failure, roll back membership and delete partial user
4. Email is **not** editable via PATCH (dedicated change flow deferred)

### Activation / deactivation

- Deactivation sets membership inactive and invalidates sessions immediately
- Reactivation does not restore roles that were intentionally removed
- Last clinic/org administrator protected (`staff.last_admin_protected`)
- Clinic admin cannot deactivate org/platform admins; org admin cannot deactivate platform admins

### Session invalidation

`ISecuritySessionInvalidationService` on deactivate, activate, role assign/remove:

- Revoke all refresh tokens for the user
- Update Identity security stamp
- Does not log token values or hashes

### Editable vs protected fields (PATCH)

**Editable:** FirstName, LastName, DisplayName, JobTitle, PhoneNumber, ExpectedVersion  
**Protected / separate flows:** OrganizationId, ClinicId, Role, Email, password, EmailConfirmed, security stamps, PATIENT linkage, platform status

## Role-assignment safety

`IRoleAssignmentAuthorizationService` (used by staff-management APIs):

- No self-elevation
- CLINIC_ADMIN cannot grant ORG/PLATFORM admin
- ORGANIZATION_ADMIN cannot grant PLATFORM_ADMIN
- Tenant scope required
- PATIENT must not mix with staff membership
- Unknown roles rejected
- Last-administrator protection on demotion/removal where applicable

### Assignable-role matrix (staff membership)

| Caller | May assign |
|--------|------------|
| CLINIC_ADMIN | DOCTOR, NURSE, RECEPTIONIST, CLINIC_ADMIN (same clinic) |
| ORGANIZATION_ADMIN | Clinic roles + ORGANIZATION_ADMIN (own org) |
| PLATFORM_ADMIN | Documented roles including PLATFORM_ADMIN only under existing safety policy + explicit bypass for cross-tenant |

PATIENT is never offered for staff membership.

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

Doctor directory and available-slots require authentication + `availability.read` (not anonymous). Staff-management routes are never public.

## Staff web application (HealthCare.Web)

MudBlazor Interactive Server app consumes the API. UI permissions (`staff.read` / `staff.manage` / `roles.read` / `roles.assign`) only control presentation; the API enforces authorization.

Pages:

- `/login` — staff sign-in
- `/dashboard` — authenticated shell home
- `/staff` — staff list/management (`staff.read` required)

MVP token storage: circuit memory + `ProtectedSessionStorage` (documented limitation; prefer BFF HttpOnly cookies later).

## Securing new endpoints

1. Add/choose a permission constant.
2. Map it in `RolePermissionMatrix` for justified roles only.
3. Annotate with `[AuthorizePermission(...)]` (and `StaffUser` / `PatientSelfScope` when needed).
4. Keep tenant/ownership checks in application services.
5. Add matrix + integration negative tests.
