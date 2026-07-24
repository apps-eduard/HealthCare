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
| `clinics.read` | Clinic discovery / directory |
| `clinics.manage` | Legacy coarse clinic management (org/platform) |
| `clinics.create` / `clinics.update` / `clinics.activate` / `clinics.deactivate` | Organization clinic CRUD and soft activation |
| `organizations.read` | PLATFORM_ADMIN organization directory search/detail |
| `organizations.select` | PLATFORM_ADMIN UI tenant selection (Web usability aid; API remains authoritative) |
| `organization_dashboard.read` | Organization Admin (and PLATFORM_ADMIN with explicit tenant bypass) operational dashboard aggregates |
| `staff.read` / `staff.manage` / `staff.password_reset` | Staff list/detail/create/update/activate + admin password-reset initiation |
| `roles.read` / `roles.assign` | Assignable-role catalog and role assignment |
| `security_sessions.revoke` | Explicit refresh-token / security-stamp revocation for in-scope staff |
| `hangfire.dashboard` | Hangfire dashboard (with PLATFORM_ADMIN) |
| `medical_notes.read` | Read authorized clinic note summaries/detail |
| `medical_notes.create` | Create draft notes for eligible appointments |
| `medical_notes.update_draft` | Update own draft |
| `medical_notes.sign` | Sign own draft |
| `medical_notes.amend` | Create signed amendment of a signed note (DOCTOR) |

## Role mappings (assumptions)

- **PLATFORM_ADMIN:** broad permissions including `organization_dashboard.read` + `organizations.read` / `organizations.select`; **does not** auto-bypass tenants — requires `PlatformAdminBypass.Explicit`. Organization directory listing is a platform operation and does **not** grant clinic/resource access. No `medical_notes.*`.
- **ORGANIZATION_ADMIN:** org-scoped ops including `organization_dashboard.read` and clinic CRUD (`clinics.create/update/activate/deactivate`); can assign roles except PLATFORM_ADMIN. **No** global organization directory.
- **CLINIC_ADMIN:** clinic-scoped ops; `clinics.read` only (no organization clinic create/update/activate/deactivate); cannot assign ORG/PLATFORM admin.
- **DOCTOR:** clinic appointments + own availability; no clinic administration.
- **NURSE:** clinical-operational appointment actions (no create/reschedule/availability manage); medical notes: `medical_notes.read/create/update_draft/sign` limited to **Nursing** note type in services.
- **RECEPTIONIST:** scheduling + search + confirm/cancel/check-in/reschedule; **no** complete/no-show; **no** availability admin.
- **PATIENT:** own profile/appointments + availability read + clinic register; no staff ops.

`staff.read` / `staff.manage` / `staff.password_reset` / `roles.read` / `roles.assign` / `security_sessions.revoke` are granted to **PLATFORM_ADMIN**, **ORGANIZATION_ADMIN**, and **CLINIC_ADMIN** only (not DOCTOR/NURSE/RECEPTIONIST/PATIENT).

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
| POST | `/staff/{id}/deactivate` | `staff.manage` | Deactivates; revokes sessions; self-deactivation denied |
| POST | `/staff/{id}/change-clinic` | `staff.manage` | Org/Platform Admin only; same-org active clinic; revokes sessions |
| POST | `/staff/{id}/password-reset` | `staff.password_reset` | Identity reset token via email abstraction; revokes sessions |
| POST | `/staff/{id}/revoke-sessions` | `security_sessions.revoke` | Explicit session invalidation |
| GET | `/roles` | `roles.read` | Roles caller may view/assign |
| POST | `/staff/{id}/roles/{roleName}` | `roles.assign` | Hierarchy-checked assignment |
| DELETE | `/staff/{id}/roles/{roleName}` | `roles.assign` | Hierarchy-checked removal (MVP requires reassignment; sole-role removal denied) |

Related auth endpoints:

| Method | Path | Notes |
|--------|------|-------|
| POST | `/api/v1/auth/complete-password-reset` | Anonymous completion with email + token + new password |
| GET | `/api/v1/auth/dev/password-reset-token` | Development-only token capture |

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

Ant Design Interactive Server app consumes the API. UI permissions (`staff.read` / `staff.manage` / `staff.password_reset` / `roles.read` / `roles.assign` / `security_sessions.revoke` / `clinics.read` / `organizations.read` / `organizations.select` / `organization_dashboard.read`) only control presentation; the API enforces authorization.

Pages:

- `/login` — staff sign-in
- `/dashboard` — authenticated shell home
- `/staff` — staff list/management (`staff.read` required)
- `/appointments`, `/appointments/calendar`, `/patients`, `/availability` — scoped operational pages

### Organization directory (platform)

- `GET /api/v1/platform/organizations` — searchable paged directory (`organizations.read`, PLATFORM_ADMIN only)
- `GET /api/v1/platform/organizations/{organizationId}` — safe detail (no billing/secrets)

Staff Web:

- `OrganizationPicker` + platform tenant banner for PLATFORM_ADMIN
- Circuit-scoped `IPlatformTenantContext` (selected OrganizationId/name, optional ClinicId, explicit bypass flag)
- Cleared on logout, auth failure, and user change — never stored in JWT claims
- Selecting an organization does **not** grant clinic/resource access; downstream APIs still require permissions + `platformAdminBypass=true` + validated ClinicId
- Ordinary tenant users do not see the organization picker

### Clinic directory

- `GET /api/v1/staff-management/clinics` — tenant-scoped clinic search (`clinics.read`)
- `GET /api/v1/staff-management/clinics/{clinicId}` — scoped clinic detail (out-of-scope → safe 404)

Tenant behavior:

- Clinic-scoped staff: own clinic only (picker read-only)
- ORGANIZATION_ADMIN: all clinics in trusted organization; optional clinic filter / “All clinics”
- PLATFORM_ADMIN: select organization via platform banner/picker, then `platformAdminBypass=true` **and** `OrganizationId` required for listing; ClinicPicker disabled until organization selected
- PATIENT: denied on staff clinic directory

Staff UI uses `ClinicPicker` / `OrganizationPicker` (no free-text ClinicId or OrganizationId). Staff Web auth is BFF-based: HttpOnly cookie + server-side token session (no browser token storage). UI is Ant Design Blazor (Fluent UI removed).

### Organization dashboard

- `GET /api/v1/organization/dashboard` — org-scoped operational aggregates (`organization_dashboard.read`)
- Optional `ClinicId`, `Date` (yyyy-MM-dd); optional `OrganizationId` **only** for PLATFORM_ADMIN with `platformAdminBypass=true`
- ORGANIZATION_ADMIN: trusted membership organization; client OrganizationId overrides rejected
- Appointment “today” uses each clinic’s local calendar date when no date is supplied and multiple clinics are in scope
- Does not return PHI beyond aggregate counts

### Organization clinic management

- `GET/POST /api/v1/organization/clinics`, `GET/PATCH /api/v1/organization/clinics/{id}`, `POST .../activate|deactivate`
- Soft deactivate preserves history; blocks new bookings and inactive-clinic membership activation; last active clinic protected
- Optional initial Clinic Admin created in the same transaction as clinic create
- `GET /api/v1/reference/timezones` — authenticated timezone catalog

## Securing new endpoints

1. Add/choose a permission constant.
2. Map it in `RolePermissionMatrix` for justified roles only.
3. Annotate with `[AuthorizePermission(...)]` (and `StaffUser` / `PatientSelfScope` when needed).
4. Keep tenant/ownership checks in application services.
5. Add matrix + integration negative tests.
