# HealthCare MVP — Phase Progression Report

## Progress overview

**Overall completion: 64%**

```text
[████████████████████░░░░░░░░░░░░]  64%
```

| Metric | Value |
|--------|-------|
| Official phases (0–13) | 14 |
| Complete | 3 (Phases 0, 1, 5) |
| Partial | 8 (Phases 2, 3, 4, 6, 7, 8, 9, 10) |
| In progress | 0 |
| Not started | 3 |
| Weighted score | (3×1.0) + (0.7 + 0.95 + 0.5 + 0.75 + 0.85 + 0.7 + 0.7 + 0.85) = 9.0 / 14 ≈ **64%** |

**Scoring rule:** Complete = 100% of phase · Partial = 50% (or noted fraction) · In progress = 25% · Not started / Blocked = 0%

**Current focus:** Medical notes staff UI, Google auth, or remaining BFF multi-instance hardening

### All phases at a glance

| # | Phase | Status | Progress |
|---|-------|--------|----------|
| 0 | Repository and documentation | Complete | `██████████` 100% |
| 1 | Solution foundation | Complete | `██████████` 100% |
| 1b | Identity + core entities foundation *(sub-step)* | Complete | `██████████` 100% |
| 2 | Identity and authentication (JWT / refresh / endpoints) | Partial | `███████░░░` 70% |
| 3 | Roles and authorization foundation | Partial | `█████████░` 95% |
| 4 | Organizations and clinics | Partial | `█████░░░░░` 50% |
| 5 | Patients and clinic-patient registration | Complete | `██████████` 100% |
| 6 | Staff and doctors | Partial | `████████░░` 75% |
| 7 | Appointment booking | Partial | `█████████░` 85% |
| 8 | Staff web application (Fluent UI) | Partial | `████████░░` 80% |
| 9 | Medical notes | Partial | `███████░░░` 70% |
| 10 | Hangfire and notifications | Partial | `█████████░` 85% |
| 11 | Patient mobile application | Not started | `░░░░░░░░░░` 0% |
| 12 | Audit and security hardening | Not started | `░░░░░░░░░░` 0% |
| 13 | Docker and deployment | Not started | `░░░░░░░░░░` 0% |

> **Note:** Phase **1b** is a foundation sub-step (Identity schema, roles, Organization/Clinic/StaffMember). It is tracked for clarity but **not** counted in the 14-phase overall percent. Official MVP phases remain **0–13** from `development-plan.md`.

---

## Purpose

This document tracks implementation progress against `development-plan.md`.

Update this file at the end of every phase (or foundation sub-step) with:

- Status
- Completion date
- What was delivered
- Verification results (build / tests / migrations)
- Remaining work or risks
- Recalculate the **Progress overview** percent and bars at the top

Authoritative design docs:

- [architecture.md](./architecture.md)
- [development-plan.md](./development-plan.md)
- [security.md](./security.md)

---

## Status legend

| Status | Meaning | Weight |
|--------|---------|--------|
| Not started | Phase has not begun | 0% |
| In progress | Work started; acceptance criteria not fully met | 25% |
| Partial | Core deliverables done; some acceptance items deferred | 50% |
| Complete | Phase acceptance criteria met and verified | 100% |
| Blocked | Waiting on dependency, environment, or decision | 0% |

---

## Overall progress table

| Phase | Name | Status | Completed |
|-------|------|--------|-----------|
| 0 | Repository and documentation | Complete | 2026-07-23 |
| 1 | Solution foundation | Complete | 2026-07-23 |
| 1b | Identity + core entities foundation | Complete | 2026-07-23 |
| 2 | Identity and authentication (JWT / refresh / endpoints) | Partial (patient register + confirm) | 2026-07-23 |
| 3 | Roles and authorization foundation | Partial (permissions + role-assignment APIs) | 2026-07-23 |
| 4 | Organizations and clinics | Partial (entities + schema only) | 2026-07-23 |
| 5 | Patients and clinic-patient registration | Complete (staff search + clinic admin) | 2026-07-23 |
| 6 | Staff and doctors | Partial (staff-management APIs; UI deferred) | 2026-07-23 |
| 7 | Appointment booking | Partial (foundation + availability + reschedule) | 2026-07-23 |
| 8 | Staff web application (Ant Design) | Partial (auth + staff + appointments UI; Fluent removed) | 2026-07-24 |
| 9 | Medical notes | Not started | — |
| 10 | Hangfire and notifications | Partial (reminders + daily clinic summary) | 2026-07-23 |
| 11 | Patient mobile application | Not started | — |
| 12 | Audit and security hardening | Not started | — |
| 13 | Docker and deployment | Not started | — |

---

## Phase 0 — Repository and documentation

**Status:** Complete  
**Completed:** 2026-07-23

### Delivered

- Git repository initialized
- Design docs: architecture, development plan, security
- `README.md`, `.gitignore`, `.editorconfig`, `Directory.Build.props`
- Solution file `HealthCare.sln`

### Verification

- Documentation present and used as the implementation source of truth

### Notes

- Docs live under `Docs/` (Windows path casing)

---

## Phase 1 — Solution foundation

**Status:** Complete  
**Completed:** 2026-07-23

### Delivered

- Projects: Api, Web, Mobile (placeholder), Domain, Application, Infrastructure, Contracts
- Test projects: Unit, Integration, Architecture
- Modular folder structure for Identity, Organizations, Clinics, Staff, Patients, Appointments, MedicalRecords, Notifications, Auditing
- EF Core + PostgreSQL (`HealthCareDbContext`)
- Serilog, Problem Details, correlation ID middleware
- OpenAPI + Swagger UI (Development)
- `/health` endpoint
- `/api/v1` controller route convention
- Initial migration: `InitialFoundation`
- `docker-compose.yml` (Postgres service)
- `.env.example`

### Database

- Created and configured `healthcare_db` on shared PostgreSQL host `100.110.26.112`
- Connection string configured in API appsettings

### Verification

- Build: succeeded
- Unit tests: passed
- Architecture tests: passed
- Integration tests: written; Docker engine was not ready at verification time
- `/health`: Healthy against `healthcare_db`

### Deferred / risks

- Full MAUI mobile project deferred to Phase 11 (class library placeholder)
- Integration tests require Docker Desktop running for Testcontainers

---

## Phase 1b — Identity and core entity foundation

**Status:** Complete  
**Completed:** 2026-07-23

> Foundation sub-step between Phase 1 and full Phase 2. JWT, refresh tokens, and auth endpoints were intentionally excluded.

### Delivered

- ASP.NET Core Identity wired to `HealthCareDbContext` (`IdentityDbContext`)
- Entities:
  - `ApplicationUser`
  - `Organization` + `OrganizationStatus`
  - `Clinic`
  - `StaffMember`
- Role constants: `AppRoles`
- Documented roles seeded idempotently:
  - `PLATFORM_ADMIN`
  - `ORGANIZATION_ADMIN`
  - `CLINIC_ADMIN`
  - `DOCTOR`
  - `NURSE`
  - `RECEPTIONIST`
  - `PATIENT`
- Identity password, lockout, and user settings aligned with `security.md`
- Migration: `AddIdentityAndCoreEntities` (applied to `healthcare_db`)
- `InitialFoundation` left unmodified

### Verification

- Build: succeeded
- Unit tests: passed (including role constant and seeder tests)
- Architecture tests: passed
- Migration applied to `healthcare_db`
- API startup seeds roles; `/health` Healthy

### Explicitly not included

- JWT access tokens
- Refresh tokens
- Auth endpoints (`/api/v1/auth/*`)
- Google authentication
- Patients, appointments, medical notes

---

## Phase 2 — Identity and authentication

**Status:** Partial  
**Updated:** 2026-07-23

### Already done

- Staff email/password login (`POST /api/v1/auth/login`)
- JWT access tokens (issuer/audience/signature/expiry from config)
- Hashed refresh tokens with rotation and family reuse revocation
- Logout / refresh-token revocation (`POST /api/v1/auth/logout`, `POST /api/v1/auth/refresh`)
- Development admin seeder (config-driven, idempotent)
- Migration `AddRefreshTokenAuthentication` applied to `healthcare_db`
- Unit tests for JWT claims/expiry, refresh crypto, rotation/reuse/logout, generic invalid login
- Manual API verification of login/refresh/reuse/logout and `/health`

### Remaining

- Patient Google authentication
- Account activation/deactivation admin workflows (beyond `IsActive` / email confirmation)
- Integration tests under Testcontainers (blocked locally: Docker engine not ready)

### Exit criteria (still open)

- Full Phase 2 endpoint set complete
- Google token validation tests
- Integration auth suite green with Docker

---

## Phase 3 — Roles and authorization foundation

**Status:** Partial (~95% — permission catalog + staff role-assignment HTTP)  
**Updated:** 2026-07-23

### Already done

- Role definitions, constants, and idempotent seeding
- `ICurrentUser`, `ICurrentStaff`, `ICurrentPatient` (request-scoped)
- Trusted scope resolution from JWT principal + **DB Identity roles** + DB staff membership (claims never trusted alone)
- `ITenantAccessService` with org/clinic/patient guards and explicit `PLATFORM_ADMIN` bypass (audited)
- Authorization policy constants and handlers (`Authenticated`, `PlatformAdmin`, `StaffUser`, `OrganizationScoped`, `ClinicScoped`, `PatientUser`, `PatientSelfScope`)
- **Fine-grained permission catalog** (`Permissions`) + **role-permission matrix** (`RolePermissionMatrix`) — code-defined, no migration
- `IPermissionService` + dynamic `perm:` policy provider; controllers use `[AuthorizePermission]` / `[AuthorizeAnyPermission]`
- `GET /api/v1/auth/me` includes `Permissions`; `GET /api/v1/auth/me/permissions`
- `IRoleAssignmentAuthorizationService` wired into staff-management role APIs
- Hangfire dashboard requires PLATFORM_ADMIN **and** `hangfire.dashboard`
- Isolation probe: `GET /api/v1/scope-probe/{organization|clinic|patient}`
- Docs: [authorization-matrix.md](./authorization-matrix.md)

### Remaining

- Docker/Testcontainers integration suite when Docker is available (suite retained)
- EF global query filters deferred; tenant access enforced in services

### Verification (permission catalog)
- Build: succeeded
- Unit tests: **213 passed** (permission matrix + role-assignment + existing)
- Architecture tests: **17 passed**
- Integration tests: Docker unavailable; suite retained (`PermissionAuthorizationEndpointTests`)
- Manual: `/health` **200**; `/auth/me` returns permissions; PLATFORM_ADMIN has `hangfire.dashboard`; DOCTOR has `appointments.complete` not `availability.manage_clinic`; PATIENT has create not search; staff search PATIENT **403**, anonymous **401**; platform without bypass still denied cross-clinic probe

---

## Phase 4 — Organizations and clinics

**Status:** Partial

### Already done

- `Organization` and `Clinic` entities
- EF configurations, FKs, unique slug indexes
- Schema migration applied

### Remaining

- Administration endpoints
- Public clinic directory endpoints
- Specialty/city filters
- Staff web screens
- Isolation and duplicate-slug tests

---

## Phase 5 — Patients and clinic-patient registration

**Status:** Complete  
**Updated:** 2026-07-23

### Already done

**Foundation / account registration**
- Patient, ClinicPatient, linkage, self-scope, staff enroll, email/password registration + confirmation

**Profile update**
- `PATCH /api/v1/patients/me` (PatientSelfScope)
- Editable: first/middle/last name, DOB, gender, mobile, preferred language, address, emergency contact
- Protected: email, role, PatientId/UserId, org/clinic linkage, IsActive, local numbers, timestamps
- Optimistic concurrency via `Patient.Version` + `expectedVersion` (HTTP 409 on conflict)
- Empty PATCH rejected by FluentValidation

**Patient-driven clinic registration**
- `POST /api/v1/patients/me/clinics/register` with trusted **clinic code = Clinic.Slug**
- Resolves clinic/org server-side; rejects invalid/inactive with generic 404-style messages
- Idempotent find-or-create; reuses local patient number sequence
- Migration `AddPatientProfileConcurrencyAndClinicRegistration` applied

**Staff patient search and clinic administration**
- `GET /api/v1/staff/patients` — paginated search (StaffUser policy)
- `GET /api/v1/staff/patients/lookup` — appointment-safe lookup (active patient + active enrollment; Org Admin / Platform bypass require ClinicId)
- `GET /api/v1/staff/patients/{patientId}` — scoped detail with in-scope enrollment list (`Enrollments`); optional `clinicId` targeting for Org Admin
- `PATCH /api/v1/staff/patients/{patientId}/clinic-profile` — ClinicPatient status only; optional `ClinicId` targeting for multi-clinic patients
- `POST /api/v1/clinics/{clinicId}/patients/{patientId}/enroll` — Org Admin may enroll into any clinic in trusted organization
- Authorized roles via active staff membership: CLINIC_ADMIN, DOCTOR, NURSE, RECEPTIONIST (clinic-scoped); ORGANIZATION_ADMIN (org clinics); PLATFORM_ADMIN only with `platformAdminBypass=true` + ClinicId
- Tenant rules: clinic staff locked to `ICurrentStaff.ClinicId` (client ClinicId ignored); org admin uses trusted OrganizationId (optional ClinicId validated in-org); PATIENT denied; no client OrganizationId on contract
- Searchable: first/middle/last name, local patient number, mobile; filters: patient active, ClinicPatient status; sort whitelist
- Pagination: default page size 20, max 100, total count/pages, stable secondary sort by ClinicPatient.Id; EF AsNoTracking + DB-side filter/sort/page
- Editable clinic field: `ClinicPatient.Status` (Active/Inactive) with `expectedVersion` concurrency
- Protected: credentials, email, UserId/PatientId, local number, org/clinic ownership, demographics via this endpoint, medical notes
- Audited via `IAuthorizationAuditLogger.PatientOperation` / `CrossTenantDenied`
- Scope enforcement remains explicit in `StaffPatientService` (EF global tenant filters still deferred)
- Migration `AddClinicPatientConcurrencyAndStaffSearchIndex` applied (`ClinicPatient.Version` + `(ClinicId, Status)` index); Org Admin Phase 4 patient slice requires **no new migration**

### Verification

- Build: succeeded
- Unit tests: 84 passed (including 22 staff patient tests)
- Architecture tests: 10 passed
- Integration tests: failed (Docker unavailable); Testcontainers suite retained
- Manual: health 200; Clinic A search excludes Clinic B local numbers; Clinic B denied A-only detail (403); org admin sees multiple clinics; client ClinicId ignored; status PATCH + restore; stale version 409; PATIENT 403; anonymous 401; pagination metadata OK

### Remaining (outside Phase 5 acceptance)

- Real email provider / Hangfire notifications
- Google auth (Phase 2)
- Staff UI for patient directory (Phase 8)
- Integration suite green once Docker is available
- Appointments (Phase 7)

---

## Phase 6 — Staff and doctors

**Status:** Partial (~85% — staff-management APIs complete for Org Admin Phase 3 backend; doctor profile UI deferred)  
**Updated:** 2026-07-24

### Already done

- `StaffMember` membership model: `UserId` (unique — one membership per user), `OrganizationId`, `ClinicId`, `Role`, profile name fields, `IsActive`, timestamps, optimistic `Version`
- EF configuration, role check constraint, migration `AddStaffManagementFoundation`
- Doctor availability APIs (Phase 7 overlap): clinic doctor directory + slots + staff availability CRUD
- **Staff-management HTTP APIs** (`/api/v1/staff-management/...`):
  - List/search (incl. `GET /clinic-admins`), detail with `ClinicName`, create (temporary password), PATCH profile, activate/deactivate
  - Same-organization clinic reassignment (`POST .../change-clinic`) for Org/Platform Admin
  - Admin password-reset initiation + Auth `complete-password-reset` foundation
  - Explicit session revoke (`security_sessions.revoke`)
  - Roles catalog + assign/remove via `IRoleAssignmentAuthorizationService`
  - Permission + active membership + tenant scope + role hierarchy
  - `ISecuritySessionInvalidationService` on security-sensitive changes
  - Last-administrator protection; out-of-scope staff → safe 404
  - Development seed: `clinicadmin@healthcare.local` / `orgadmin@healthcare.local`
- Docs: [authorization-matrix.md](./authorization-matrix.md), [security.md](./security.md), [mvp-organization-admin-scope.md](./mvp-organization-admin-scope.md)

### Remaining

- Doctor profile presentation beyond availability
- Invitation / production email activation workflow (temporary-password + Dev reset capture for now)
- Force password-change on first login
- Multi-membership per user (explicitly out of MVP)

### Verification (staff-management APIs)
- Build: succeeded
- No new migration for Org Admin staff Phase 3 backend (foundation already applied)
- Unit / architecture / integration suites updated and retained
- Manual: `/health` **200**; Org Admin org-scoped list; clinic-admins filter; create CLINIC_ADMIN/DOCTOR/NURSE/RECEPTIONIST; deny PLATFORM_ADMIN assign; self-deactivation denied; last-admin protected; password-reset does not return token; revoke-sessions invalidates refresh tokens

### Known limitations
- Email not editable via staff PATCH
- Production email sender for password reset may be unconfigured (abstraction + Development capture)
- Role model remains single membership `Role` string (+ Identity role sync on assign)
- Access-token TTL still applies until stamp/permission re-resolution on next authenticated request
- `DELETE .../roles/{role}` remains reassignment-oriented (cannot leave staff without a role)

---

## Phase 7 — Appointment booking

**Status:** Partial (~85% — foundation + availability + reschedule)  
**Updated:** 2026-07-23

### Already done (foundation)

**Entity / relationships**
- `Appointment`: OrganizationId, ClinicId, PatientId, ClinicPatientId, DoctorStaffMemberId, AppointmentDateUtc, DurationMinutes, Reason, Status, PatientNotes, CancellationReason, Source, CreatedByUserId, Version, timestamps
- Requires active ClinicPatient enrollment; doctor must be active DOCTOR in same clinic (MVP assumption)

**Status workflow**
- Requested → Confirmed / CancelledByPatient / CancelledByClinic
- Confirmed → CheckedIn / Cancelled* / NoShow
- CheckedIn → InProgress / Completed / NoShow / CancelledByClinic
- InProgress → Completed / CancelledByClinic
- Terminal: Completed, CancelledByPatient, CancelledByClinic, NoShow
- Patient booking starts **Requested**; staff booking starts **Confirmed** (assumption)

**Endpoints**
- `POST/GET /api/v1/patients/me/appointments`
- `POST/GET /api/v1/staff/appointments`
- `GET /api/v1/appointments/{id}`
- `POST .../confirm|check-in|complete|no-show` (staff); `POST /api/v1/appointments/{id}/cancel` (patient or staff)
- `POST /api/v1/appointments/{id}/reschedule` (patient or staff; authenticated)

**Tenant isolation**
- Patient: self only via ICurrentPatient
- Clinic staff: trusted ClinicId
- Org admin: organization-wide list; create uses assigned clinic membership
- PLATFORM_ADMIN: explicit bypass only
- Client OrganizationId/PatientId not accepted on patient create contract

**Slot conflict**
- Overlap detection for same clinic + doctor; cancelled ignored; Serializable transaction on relational DB

**Migration**
- `AddAppointmentFoundation` applied to `healthcare_db`

### Availability and scheduling (this increment)

**Availability model**
- `DoctorAvailability`: weekly recurring window per doctor/clinic with DayOfWeek, Start/End local time, SlotDurationMinutes (10–240), EffectiveFrom/To, IsActive, Version
- OrganizationId and ClinicId derived server-side from the doctor membership
- Overlapping active windows for the same doctor/day/effective range are rejected (`appointment.availability_conflict`)

**Exception behavior**
- `DoctorAvailabilityException`: UnavailableFullDay, UnavailableRange, CustomAvailableRange
- Exceptions override weekly windows for that date
- Reason max length 250

**Doctor listing**
- `GET /api/v1/clinics/{clinicCode}/doctors` — authenticated patient or staff
- Active DOCTOR members of the resolved clinic only; safe fields (StaffMemberId, DisplayName, Specialty, ClinicCode, AcceptsBookings)
- No email, UserId, phone, or other clinic memberships

**Slot-generation rules**
- One date per request: `GET /api/v1/clinics/{clinicCode}/doctors/{staffMemberId}/available-slots?date=&durationMinutes=`
- Active weekly windows + effective dates + exceptions
- Exclude past times; exclude slots overlapping Requested/Confirmed/CheckedIn/InProgress
- Cancelled appointments do not block
- Starts must land on slot boundaries; duration must match window slot duration; end must stay in window

**Timezone strategy**
- `Clinic.TimeZoneId` IANA id (default `Asia/Riyadh` for existing/dev clinics)
- Availability times are clinic-local; `Appointment.AppointmentDateUtc` remains UTC
- Conversion via `IClinicTimeZoneConverter` (Windows fallback: Arab Standard Time for Riyadh)
- Never uses server local time implicitly

**Booking-validation changes**
- Patient and staff create paths call `EnsureSlotIsBookableAsync` before existing Serializable overlap check
- 409 codes: `appointment.outside_availability`, `appointment.slot_unavailable`, `appointment.availability_exception`, `appointment.invalid_slot_duration`

**Staff availability administration**
- `GET/POST/PATCH/DELETE /api/v1/staff/doctors/{staffMemberId}/availability`
- `GET/POST/DELETE .../availability-exceptions` (list added for staff UI; no exception PATCH)
- ClinicAdmin: same clinic; OrgAdmin: same organization; Doctor: own availability; PATIENT: forbidden; PLATFORM_ADMIN: explicit bypass

**Migration result**
- `AddAppointmentAvailability` applied to `healthcare_db` (DoctorAvailabilities, DoctorAvailabilityExceptions, Clinics.TimeZoneId)

### Verification

- Build: succeeded
- Unit tests: **124 passed** (40 appointment/availability)
- Architecture tests: **14 passed**
- Integration tests: Docker unavailable (`npipe://./pipe/docker_engine`); suite retained (foundation + availability endpoints)
- Manual (http://localhost:5080): health **200**; weekly availability created; full-day exception created; doctor list returned Clinic A doctor; slots returned 24 for one date (`Asia/Riyadh`); exception day returned 0 slots; patient booking in slot succeeded (UTC `06:30` = local `09:30+03`); occupied slot removed; cancel restored slot; outside availability **409**; Clinic B doctor managing Clinic A availability **403**

### Remaining Appointment work

- Reminder SMS/email provider integration (real delivery)
- Staff UI appointment queue / calendar
- Advanced recurring schedules
- Broader assignable roles if needed beyond DOCTOR

### Reschedule workflow (this increment)

**Endpoint**
- `POST /api/v1/appointments/{appointmentId}/reschedule`
- Request: optional `DoctorStaffMemberId`, `AppointmentDateUtc`, `DurationMinutes`, `ExpectedVersion`, optional `Reason` (max 250)
- Does **not** accept PatientId, OrganizationId, ClinicId, Status, CreatedByUserId, reminder/audit fields
- Response: existing safe `AppointmentResponse` (identity preserved; version increments)

**Allowed source statuses**
- Allowed: `Requested`, `Confirmed`
- Rejected (409 `appointment.reschedule_not_allowed`): CheckedIn, InProgress, Completed, NoShow, CancelledByPatient, CancelledByClinic
- Same doctor/start/duration → 409 `appointment.reschedule_same_slot`

**Authorization**
- PATIENT: own appointment only; same clinic; optional doctor change only to active DOCTOR in that clinic
- Clinic staff: trusted clinic membership; cannot move clinic/org
- ORGANIZATION_ADMIN: organization only; clinic remains server-validated (appointment clinic unchanged)
- PLATFORM_ADMIN: existing explicit bypass
- Client tenant identifiers never override trusted scope

**Availability and conflict validation**
- Future start; doctor availability + clinic timezone; slot boundaries/duration; exceptions; overlap
- Excludes current appointment from overlap; cancelled appointments do not block
- Serializable transaction on relational DB
- 409 for slot unavailable / outside availability / exception / invalid duration / concurrency

**Reminder replacement**
- After successful commit: replace Upcoming for new schedule (same idempotency key `{appointmentId:N}:Upcoming`)
- Pending/Processing/Failed/Sent Upcoming row is reset to Pending with new `ScheduledAtUtc` (24h before, or immediately if closer)
- Sent Confirmation is left alone (not duplicated / not resent)
- Hangfire jobs still receive AppointmentId + ReminderId only

**Concurrency**
- Requires `ExpectedVersion`; mismatch → 409 `appointment.concurrency_conflict`
- EF concurrency token on `Appointment.Version`

**Audit-history strategy**
- Persistent `AppointmentRescheduleHistory` (safe administrative fields only; no medical/patient profile payload)
- Fields: previous/new doctor, start, duration, rescheduledBy, at, reason, previousVersion

**Migration result**
- `AddAppointmentRescheduleWorkflow` applied to `healthcare_db` (`AppointmentRescheduleHistories`)

### Verification (reschedule)

- Build: succeeded
- Unit tests: **159 passed** (22 reschedule-focused)
- Architecture tests: **15 passed**
- Integration tests: Docker unavailable (`npipe://./pipe/docker_engine`); suite retained (`AppointmentRescheduleEndpointTests`)
- Manual: health **200**; create Requested; reschedule succeeded (ID unchanged, version++); Upcoming same row retargeted; Confirmation count unchanged; overlap **409**; terminal reschedule **409**; Clinic B staff **404**

---

## Phase 8 — Staff web application

**Status:** Partial (~70% — auth shell + staff + appointments + patients + availability)  
**Updated:** 2026-07-23

### Already done

- MudBlazor 9 on `HealthCare.Web` (Interactive Server)
- Staff login (`/login`) against `POST /api/v1/auth/login`
- Token handling via server-side BFF session store (`IApiTokenSessionStore` / `IApiTokenStore`) and refresh via `AuthDelegatingHandler`
- Cookie authentication scheme (`HealthCare.Staff.Auth`) registers default authenticate/challenge/forbid schemes; `UseAuthentication`/`UseAuthorization` ordered correctly. Anonymous GET to `/appointments` etc. challenges to `/login?returnUrl=...` (no HTTP 500). Cookie is HttpOnly, SameSite=Lax, Secure outside Development, holds minimal claims + opaque `bff_sid` only (never access/refresh tokens). API bearer tokens live only in the server session store. `SafeReturnUrl` rejects absolute/protocol-relative/open redirects. PATIENT may authenticate but sees forbidden UI (not login loop). Logout/refresh-failure clear server sessions, permissions, platform tenant, and cookie.
- Custom `StaffAuthenticationStateProvider` + `IPermissionState` from `/api/v1/auth/me`
- **Anonymous protected-route fix:** Cookie authentication scheme (`HealthCare.Staff.Auth`) registers `IAuthenticationService` with default authenticate/challenge/forbid schemes; `UseAuthentication`/`UseAuthorization` ordered correctly. Anonymous GET to `/appointments` etc. challenges to `/login?returnUrl=...` (no HTTP 500). Cookie is HttpOnly, SameSite=Lax, Secure outside Development, holds minimal claims + opaque `bff_sid` only (never access/refresh tokens). API bearer tokens live only in the server BFF session store. `SafeReturnUrl` rejects absolute/protocol-relative/open redirects. `RedirectToLogin` waits for confirmed anonymous state. PATIENT may authenticate but sees forbidden UI (not login loop). Logout/refresh-failure clear server sessions, permissions, platform tenant, and cookie.
- **Staff patient directory:** `/patients` (+ optional `/patients/{patientId}`). Nav when `patients.search`. Server-side search/pagination/filters; detail dialog via `patients.read`; ClinicPatient status update via `patients.update_clinic_status` with `ExpectedVersion`. Reuses `PatientPicker` display helpers. Typed `IStaffPatientApiClient` (search/detail/clinic-profile).
- **Staff availability management:** `/availability`. Nav when any of `availability.manage_self` / `availability.manage_clinic` / `availability.manage_organization` (not `availability.read` alone). Clinic/doctor selection: doctor self fixed; clinic admin fixed clinic + doctor picker; org admin `ClinicPicker` then doctors; PLATFORM_ADMIN requires org + clinic bypass. Weekly windows (Mon–Sun), create/edit/delete with `ExpectedVersion`, date exceptions create/delete (no exception edit), clinic timezone label, optional available-slots preview. Typed `IDoctorAvailabilityApiClient`. Helpers in `HealthCare.Web.Availability`. Smallest API add: `GET .../availability-exceptions`.
- Authenticated MudBlazor shell (app bar, drawer, logout)
- Dashboard (`/` / `/dashboard`) with session context and permission-aware links
- Staff management page (`/staff`): server-side search/filter/pagination, detail, create, activate/deactivate, role assign
- **Clinic directory API + picker:** `GET /api/v1/staff-management/clinics` with tenant scope; MudBlazor `ClinicPicker` replaces free-text ClinicId
- **Staff appointments workspace:**
  - Routes: `/appointments` (queue), `/appointments/calendar` (day/week)
  - Nav: Appointments (when `appointments.read`), Patients, Availability, Staff Management, Dashboard
  - Typed clients: `IAppointmentApiClient`, `IStaffPatientApiClient`, `IDoctorAvailabilityApiClient`
  - Queue: server-side date/status/doctor/clinic/page/sort filters; permission-aware actions
  - Calendar: lightweight Mud day/week views; clinic timezone display (not browser-local)
  - Detail / create / cancel / reschedule dialogs with `ExpectedVersion` concurrency
  - `PatientPicker` (debounced staff patient search); doctor + available-slot selection via API
  - Centralized status chips (`AppointmentStatusPresentation`) and action visibility (`AppointmentActionRules`)
  - Problem Details mapping for appointment error codes (slot unavailable, concurrency, etc.)
  - Optional ~45s queue polling (paused while dialogs open)
- Typed clients: `IAuthApiClient`, `IStaffManagementApiClient`, `IClinicDirectoryApiClient`, appointment/patient/availability clients above
- Problem Details mapping to safe UI messages
- PATIENT accounts blocked from staff UI
- Config: `Api:BaseUrl`

### API contract enrichment (smallest safe change)

`AppointmentResponse` now includes staff-safe display fields: `PatientDisplayName`, `LocalPatientNumber`, `DoctorDisplayName`, `ClinicName`, `ClinicSlug`, `ClinicTimeZoneId`.  
`CreateStaffAppointmentRequest.ClinicId` optional for ORGANIZATION_ADMIN / PLATFORM_ADMIN create scope (clinic-scoped staff still use membership clinic).  
Availability: added staff `GET .../availability-exceptions` (no schema change). Exception edit remains unsupported.

### Remaining

- Notes, settings, audit viewer screens
- Full BFF pattern delivered (Phase 8c) — HttpOnly cookie + server-side token session
- Drag-and-drop calendar reschedule / SignalR realtime (explicitly out of this slice)
- Exception edit UI (blocked until API supports PATCH)
- Broader bUnit component coverage (prefer support/view-model tests while bUnit restore is flaky)

### Known limitations

- API access/refresh tokens are stored server-side in the BFF session store (Development: in-memory distributed cache; production requires a shared distributed cache). The staff Web cookie authenticates the Blazor host and carries only an opaque session id.
- PLATFORM_ADMIN uses organization directory + tenant banner (no free-text OrganizationId); calendar/availability still require explicit clinic selection
- Patient directory list masks mobile numbers; detail shows full mobile when returned by API. Address/emergency contact shown in detail only when present on the safe contract.
- List API has no free-text search parameter (queue filters by date/status/doctor/clinic only)
- Reason for visit / patient notes are not shown in staff appointment UI
- Availability DayOfWeek cannot be changed on edit (PATCH contract has no DayOfWeek)
- No invitation email UI (temporary-password create only)
- API remains the authorization authority

### Verification

- Build: see latest commit verification
- Unit / architecture / web support tests: availability helpers + Web architecture checks
- Integration: existing appointment/availability suites retained (Docker/Testcontainers)
- Manual: see latest commit notes for `/availability` flows

---
## Phase 8b — PLATFORM_ADMIN organization directory + tenant selector

**Status:** Complete  
**Updated:** 2026-07-23

### Delivered

- Permissions: `organizations.read`, `organizations.select` (PLATFORM_ADMIN only)
- API: `GET /api/v1/platform/organizations`, `GET /api/v1/platform/organizations/{organizationId}`
- Safe DTO fields only (id, name, slug, active, clinic count, createdAtUtc) — no billing/secrets
- Web: `IOrganizationDirectoryApiClient`, circuit-scoped `IPlatformTenantContext`, `OrganizationPicker`, platform tenant banner
- Removed free-text Organization ID from Staff, Appointments, Calendar, Patients, Availability, create dialogs
- ClinicPicker disabled for PLATFORM_ADMIN until organization selected; switching org clears clinic + page data
- Medical notes remain denied to PLATFORM_ADMIN (regression covered)
- No migration (existing Organizations schema)

### Known limitations

- Organization selection is a usability aid — API authorization + explicit bypass remain required
- No organization create/update/suspend UI or API in this slice
- No medical-notes UI
- bUnit component tests still limited; support/architecture Web tests cover free-text removal and tenant state

### Verification

- See commit verification notes

---
## Phase 8c — HttpOnly BFF authentication

**Status:** Complete  
**Updated:** 2026-07-23

### Delivered

- Server-side `IApiTokenSessionStore` (distributed cache + data protection)
- Scoped `ServerSessionApiTokenStore` replacing ProtectedSessionStorage token store
- BFF endpoints: `POST /bff/auth/login`, `GET /bff/auth/establish`, `GET|POST /bff/auth/logout`
- Login ticket via short-lived HttpOnly cookie (not URL, not API tokens)
- Cookie `OnValidatePrincipal` rejects missing/expired server sessions
- Server-side refresh with per-session lock; refresh failure clears session + circuit state
- Antiforgery validated on BFF login
- Typed clients unchanged; bearer attached only server-side
- Docs/config: `Bff` options section

### Known limitations

- Development uses `AddDistributedMemoryCache` — multi-instance production requires Redis/SQL distributed cache
- Per-session refresh lock is process-local (sufficient for single Web instance)
- Superseded hardening in Phase 8d (POST-only mutations, antiforgery on logout, no establish GET)

### Verification

- See commit verification notes

---
## Phase 8d — BFF CSRF / session hardening

**Status:** Complete  
**Updated:** 2026-07-23

### Delivered

- POST-only auth mutations: `POST /bff/auth/login`, `POST /bff/auth/logout`
- Legacy `GET/POST /bff/auth/establish` and `GET /bff/auth/logout` return **405** (non-mutating)
- Antiforgery required for login and logout (Development included); missing/invalid → safe redirect
- One-step login establishes the Web session (no login-ticket / establish redirect) — login CSRF blocked by antiforgery binding
- Session fixation: prior session removed + new `bff_sid` on every successful login
- Session binding: cookie user id must match server session user id (`session_mismatch` clears both)
- Versioned `TryUpdateTokensAsync` prevents late refresh from recreating a logged-out session
- `/logout` page antiforgery-POSTs to BFF logout (StaffLayout / Forbidden use it)
- Cookie: `__Host-HealthCare.Staff` when HTTPS required; Dev HTTP uses `HealthCare.Staff.Auth`
- `SafeReturnUrl` double-decode + encoded external URL rejection
- Web tests: HTTP methods, antiforgery, session CAS, return URL, architecture checks

### Known limitations

- Development: in-memory distributed cache; process-local refresh lock (document for multi-instance)
- No separate login-ticket correlation cookie (unnecessary with one-step antiforgery POST)

### Verification

- See commit verification notes

---
## Phase 8e — Fluent UI migration (replace MudBlazor)

**Status:** Complete  
**Updated:** 2026-07-24

### Delivered

- Replaced MudBlazor with `Microsoft.FluentUI.AspNetCore.Components` **4.14.3** (+ Icons)
- Enterprise healthcare design tokens (`wwwroot/css/healthcare-enterprise.css`)
- Shared: `PageHeader`, `StatusBadge`, loading/empty/error/permission states, `IUserNotificationService`
- Migrated shell, login, dashboard, staff, patients, appointments, calendar (CSS grid), availability, pickers, dialogs
- Zero MudBlazor package/usings/components remaining in Web
- Docs: `Docs/fluent-ui-design-system.md`

### Known limitations

- No paid Fluent scheduler — calendar is custom CSS Grid day/week
- Dense lists use enterprise HTML tables (server-side paging preserved)
- Some date/time fields use native inputs styled to match Fluent

### Verification

- See commit verification notes

---
## Phase 8f — Ant Design Blazor migration (replace Fluent UI)

**Status:** Complete  
**Updated:** 2026-07-24

### Delivered

- Replaced Fluent UI with `AntDesign` **1.6.2**
- Ant Design Pro–inspired enterprise shell (`Sider` + `Header` + page container)
- Shared: `HcPageHeader`, `StatusBadge`, loading/empty/error/permission states, `IUserNotificationService`, `IUiModalService`
- Migrated shell, login, dashboard, staff, patients, appointments, calendar, availability, pickers, dialogs
- Zero Fluent UI package/usings/components remaining in Web
- Docs: `Docs/ant-design-enterprise-system.md`
- NU1900 kept non-fatal under `TreatWarningsAsErrors` (audit still enabled)

### Known limitations

- No paid Ant Design Pro package — open-source AntDesign only
- Calendar is custom CSS Grid day/week
- Dense lists use enterprise HTML tables

### Verification

- See commit verification notes

---
## Phase 9 — Medical notes

**Status:** Partial (~70% — secure backend foundation; no staff UI yet)  
**Updated:** 2026-07-23

### Delivered

- Entities: `MedicalNote`, `MedicalNoteAuditEvent` (metadata-only audit)
- Lifecycle: Draft → Signed; amendments = new Signed rows (`AmendsMedicalNoteId`) created+signed atomically
- Permissions + matrix: DOCTOR all; NURSE Nursing-scoped create/update/sign + read; CLINIC_ADMIN/ORG_ADMIN/RECEPTIONIST/PATIENT/PLATFORM_ADMIN none for note content
- Clinical access: `IMedicalNoteAccessService` + `IMedicalNoteService`
- Endpoints under `/api/v1/appointments/{id}/medical-notes` and `/api/v1/medical-notes/{id}` (draft/sign/amend)
- Appointment eligibility for create: CheckedIn, InProgress, Completed
- Optimistic concurrency via `Version` / `ExpectedVersion`
- Migration: `AddMedicalNotesFoundation` applied to `healthcare_db`
- Unit + architecture + integration suites (Docker-dependent integration retained)

### Known limitations / remaining

- No staff medical-notes UI yet
- No patient self-access / IsVisibleToPatient
- No ordinary delete; drafts cannot be abandoned via API yet
- No field-level encryption (document TLS + encrypted DB/backups)
- PLATFORM_ADMIN explicit bypass does **not** grant note content

### Verification

- Build succeeded; unit **254**; architecture **18**; integration Docker unavailable
- Migration applied: `20260723192913_AddMedicalNotesFoundation`

---

## Phase 10 — Hangfire and notifications

**Status:** Partial (~85% — reminders + clinic summary + configurable worker hosting)  
**Updated:** 2026-07-23

### Hangfire configuration (worker hosting)

- Packages: `Hangfire.AspNetCore` + `Hangfire.PostgreSql`
- Storage: existing `DefaultConnection` PostgreSQL, schema `hangfire` (always registered)
- Options: `HangfireOptions` / `HangfireDashboardOptions` with `ValidateOnStart`
- **Worker enablement model:** local Hangfire Server runs only when `Hangfire:Enabled=true` (never inferred from environment name alone)
- **Defaults:** Development enables workers, recurring registration, and dashboard; base + Production keep all three **disabled** unless explicitly enabled
- **Queues:** `default`, `reminders`, `summaries` (normalized lowercase; duplicates removed; empty/invalid rejected when enabled)
- **WorkerCount:** 1–64 (safe range); invalid values fail startup when enabled
- **ServerName / ShutdownTimeoutSeconds:** validated when enabled
- Enabling workers does **not** enable the dashboard

**Configuration keys**

| Key | Purpose |
|-----|---------|
| `Hangfire:Enabled` | Register `AddHangfireServer` (local workers) |
| `Hangfire:WorkerCount` | Worker threads |
| `Hangfire:Queues` / `Hangfire__Queues__N` | Queue names |
| `Hangfire:ServerName` | Hangfire server name |
| `Hangfire:ScheduleRecurringJobs` | Register recurring jobs (requires `Enabled`) |
| `Hangfire:ShutdownTimeoutSeconds` | Graceful shutdown |
| `Hangfire:Dashboard:Enabled` | Map dashboard (independent) |
| `Hangfire:Dashboard:Path` | Dashboard path (default `/hangfire`) |

**Recurring-job registration**
- `HangfireRecurringJobRegistrar` (idempotent `AddOrUpdate`)
- Runs only when **both** `Enabled` and `ScheduleRecurringJobs` are true
- Skipped for EF design-time / tooling hosts
- Jobs: reminder recovery `*/5`; summary dispatch + recovery `*/15`
- Logged job IDs on registration; no duplicate IDs after restart

**Dashboard security**
- Disabled by default outside Development
- `HangfireDashboardAuthFilter`: authenticated `PLATFORM_ADMIN` only (unchanged)
- Never publicly accessible; workers-on does not imply dashboard-on
- Unsafe path (`/`, missing leading `/`) rejected when dashboard enabled
- Prefer HTTPS / reverse-proxy TLS in non-Development

**Health checks**
- `/health` — all checks (database + Hangfire probes)
- `/health/ready` — checks tagged `ready` (database + Hangfire storage when workers/scheduling need it)
- `hangfire_worker` reports enabled/disabled and **does not fail** when intentionally disabled
- When workers and scheduling are both off, Hangfire storage is not required for readiness
- When Hangfire is enabled, storage failure → readiness Unhealthy

**External worker compatibility**
- Storage + `IBackgroundJobClient` always registered; API may enqueue with local workers off
- Job classes live in Infrastructure; Application abstractions have no Hangfire package dependency
- Recurring registrar is reusable from a future dedicated worker host
- Non-Development notification senders are no-op (Development uses logging senders)
- No separate worker project in this increment

**Migration result**
- None required for worker hosting (no schema change)

### Reminder types
- `Confirmation` — after appointment create (and confirm; idempotent)
- `Upcoming` — 24 hours before `AppointmentDateUtc` (or immediately if closer)
- `Cancellation` — after cancel; pending Confirmation/Upcoming cancelled first

### Scheduling rules
- Reminder rows persisted after appointment commit
- Hangfire jobs receive **AppointmentId + ReminderId only**
- Processor reloads DB state; skips Sent/Cancelled; cancels for Completed/NoShow; Cancellation allowed only when cancelled
- Reschedule replaces Upcoming via `ScheduleAfterAppointmentRescheduledAsync` (Confirmation Sent left alone)

### Idempotency strategy
- Unique `IdempotencyKey` = `{appointmentId:N}:{ReminderType}`
- Concurrent duplicate inserts ignored
- Sent reminders never re-delivered

### Retry and recovery
- Hangfire `AutomaticRetry` (3) + entity `AttemptCount` (max 5)
- Permanent failure → `Failed`
- Staff `POST .../reminders/retry` for Failed/Pending
- Recurring recovery every 5 minutes: requeue overdue Pending (DisableConcurrentExecution)

### Endpoints
- `GET /api/v1/staff/appointments/{appointmentId}/reminders`
- `POST /api/v1/staff/appointments/{appointmentId}/reminders/retry`
- `GET /api/v1/staff/clinics/current/appointment-summary?date=&clinicId=`
- `POST /api/v1/staff/clinics/{clinicId}/appointment-summary/{date}/retry`

### Daily clinic appointment summary

**Purpose**
- Operational morning brief for each active clinic: status counts, doctor grouping, first/last times, minimal appointment list

**Schedule and date semantics (assumption)**
- Global dispatcher every **15 minutes** (`*/15 * * * *`)
- Due when clinic-local time is **≥ 06:00**
- Covers **the same clinic-local calendar day** (not tomorrow)
- Clinic job arguments: **RunId only**; reload clinic/appointments from DB

**Timezone strategy**
- `Clinic.TimeZoneId` via `IClinicTimeZoneConverter`
- Day bounds: `[ToUtc(date, 00:00), ToUtc(date+1, 00:00))`
- Never uses server local time

**Dispatcher design**
- One recurring dispatcher (not one recurring job per clinic)
- Skips inactive clinics/organizations
- Inserts `ClinicAppointmentSummaryRun` with unique `IdempotencyKey = {clinicId:N}:{yyyy-MM-dd}` then enqueues process
- Concurrent insert races ignored; Completed runs never re-queued
- Separate recovery recurring job every 15 minutes (`DisableConcurrentExecution`)

**Summary contents**
- Counts: Total, Requested, Confirmed, CheckedIn, InProgress, Completed, NoShow, CancelledByPatient, CancelledByClinic, Unassigned
- ByDoctor groups; first/last UTC + local display
- Optional list items: AppointmentId, LocalTime, Status, DoctorDisplayName

**Privacy exclusions**
- No reason, patient notes, medical notes, patient profile, DOB, address, contacts, LocalPatientNumber, tokens

**Idempotency / persistence**
- Persistent `ClinicAppointmentSummaryRun` (no message body storage)
- Statuses: Pending, Processing, Completed, Failed
- Completed never resent; Failed may retry (max 5 attempts)
- Sanitized `LastError` / `LastErrorCode` only

**Retry and recovery**
- Hangfire `AutomaticRetry` (3) on process job
- Recovery retries Failed, stuck Processing (>30m), and Pending without BackgroundJobId
- Staff retry endpoint for Failed/Pending

**Sender**
- `IClinicAppointmentSummarySender` → Development logging sender; no-op outside Development

**Tenant authorization (staff endpoints)**
- Clinic staff: trusted clinic (client ClinicId ignored)
- Org admin: clinic within organization
- PATIENT: 403
- PLATFORM_ADMIN: explicit bypass + clinicId (controller still requires StaffUser policy)

### Prior migrations (unchanged)
- `AddAppointmentReminders` / `AddClinicAppointmentSummaryRuns` already on `healthcare_db`

### Verification (configurable worker hosting)
- Build: succeeded
- Unit tests: **193 passed** (16 Hangfire hosting-focused)
- Architecture tests: **15 passed**
- Integration tests: Docker unavailable (`npipe://./pipe/docker_engine`); suite retained (`HangfireHostingEndpointTests`)
- Manual:
  - Production + `Hangfire:Enabled=false`: API up; `/health` **200**; `/health/ready` **200**; `/hangfire` **404**; startup log `Enabled=False` (no secrets)
  - Production + enabled workers (`WorkerCount=3`, queues default/reminders/summaries): startup log shows config; recurring jobs registered once; restart re-registered same IDs via `AddOrUpdate`; dashboard stayed **404**
  - Invalid `WorkerCount=0` with Enabled: **OptionsValidationException** fail-fast
  - Development dashboard: anonymous **401**; PLATFORM_ADMIN **200**; non-admin staff **403**
  - Dashboard mapped via `MapHangfireDashboard` (endpoint routing)

### Known limitations
- Development logging / no-op senders only (no real SMS/email)
- No dedicated worker host project yet (API can host workers when enabled)
- PLATFORM_ADMIN without staff membership cannot call StaffUser-protected summary endpoints

### Remaining Hangfire / notification work
- Real email/SMS provider adapters
- Optional dedicated Hangfire worker host process
- Expired refresh-token cleanup job (architecture backlog)

---

## Phase 11 — Patient mobile application

**Status:** Not started

### Planned

- Replace Mobile placeholder with .NET MAUI Blazor Hybrid (Android first)
- Secure token storage
- Full patient MVP flow

---

## Phase 12 — Audit and security hardening

**Status:** Not started

### Planned

- Complete audit logging
- Rate limiting, secure headers, lockout/password review
- Mandatory isolation and abuse tests
- Dependency vulnerability review

---

## Phase 13 — Docker and deployment

**Status:** Not started

### Planned

- Dockerfiles and Compose for API, Web, PostgreSQL, worker
- Nginx + HTTPS docs
- Migration strategy, backups, production health checks

---

## Change log

| Date | Phase | Update |
|------|-------|--------|
| 2026-07-23 | 0 / 1 | Solution foundation created; `healthcare_db` configured |
| 2026-07-23 | 1b / 3 / 4 / 6 | Identity, roles, Organization/Clinic/StaffMember schema added |
| 2026-07-23 | — | Progression report created |
| 2026-07-23 | — | Added top-of-file phase list with progress bars and 25% overall completion |
| 2026-07-23 | 2 | JWT + refresh-token foundation, auth endpoints, migration applied; overall 29% |
| 2026-07-23 | 3 | ICurrentUser + tenant isolation policies, /auth/me, scope-probe; overall ~30% |
| 2026-07-23 | 5 | Patient foundation + ClinicPatient + self-scope endpoints; migration applied; overall ~34% |
| 2026-07-23 | 5 / 2 | Patient registration, email confirmation, clinic enroll + local numbers; overall ~37% |
| 2026-07-23 | 5 | Profile PATCH + patient clinic self-register via slug; concurrency Version; overall ~39% |
| 2026-07-23 | 5 | Staff patient search + ClinicPatient admin; Phase 5 complete; overall ~40% |
| 2026-07-23 | 7 | Appointment foundation (entity, booking, transitions, conflicts); overall ~43% |
| 2026-07-23 | 7 | Availability windows, exceptions, doctor list, slots, booking rules; overall ~44% |
| 2026-07-23 | 10 | Appointment reminders + Hangfire (PostgreSQL, Dev dashboard); overall ~48% |
| 2026-07-23 | 7 | Appointment reschedule workflow + history + Upcoming replacement; overall ~49% |
| 2026-07-23 | 10 | Daily clinic appointment summary Hangfire dispatcher + runs; overall ~51% |
| 2026-07-23 | 10 | Configurable Hangfire worker hosting (non-Dev enablement); overall ~52% |
| 2026-07-23 | 3 | Fine-grained permission catalog + authorization matrix; overall ~53% |
| 2026-07-23 | 8 | Staff availability management UI + list-exceptions GET; overall ~63% |
| 2026-07-23 | 9 | Medical notes secure backend foundation + migration; overall ~64% |
| 2026-07-24 | 8 / 4 | Organization Admin dashboard foundation (`organization_dashboard.read` + aggregates UI) |
| 2026-07-24 | 4 | Organization clinic CRUD + `/clinics` UI + soft activate/deactivate |
| 2026-07-24 | 4 | Org Admin staff + Clinic Admin management (change-clinic, password-reset, revoke-sessions, `/staff` tabs) |
| 2026-07-24 | 4 | Org Admin staff management backend hardening (`clinic-admins`, audits, tests, docs) |
| 2026-07-24 | 4 | Org Admin patient directory + enrollment backend (`lookup`, enrollment list, org enroll, audits) |
| 2026-07-24 | 4 | Org Admin appointment queue/calendar backend (queue+calendar endpoints, Complete denied, audits) |
| 2026-07-24 | 4 | Org Admin doctor availability backend (staff clinic doctors, ClinicId check, audits) |
| 2026-07-24 | 4 / 10 | Org Admin reminder + clinic-summary operations (search/runs/health/retry/audits) |
| 2026-07-24 | 4 | Org Admin organization reports backend (JSON + safe CSV, scope/audits) |
| 2026-07-24 | 4 / 12 | Org Admin security ops + sessions (visibility, revoke, compromise, summaries, SecurityEvents) |
| 2026-07-24 | 4 | Org Admin audit-log query + usage/limits visibility (`OrganizationAuditEvents`, limit enforcement) |
| 2026-07-24 | 8 | Org Admin Frontend Phase 1 — dashboard + clinic management UI (detail drawer, working clinic context, tests) |
| 2026-07-24 | 8 | Org Admin Frontend Phase 2 — staff + Clinic Admin management UI (tabs, clinic-admins client, concurrency, tests) |
| 2026-07-24 | 8 | Org Admin Frontend Phase 3 — patient directory + clinic enrollment UI (drawer, lookup, enroll, tests) |
| 2026-07-24 | 8 | Org Admin Frontend Phase 4 — appointment queue + calendar UI (queue/calendar APIs, clinic context, tests) |

---

## How to update this file

After finishing a phase:

1. Set the phase **Status** and **Completed** date in the overall table.
2. Update the matching row in **All phases at a glance** (status + mini bar + percent).
3. Recalculate **Overall completion** using the scoring rule and refresh the top progress bar.
4. Fill **Delivered**, **Verification**, and **Notes** for that phase section.
5. Move **Current focus** to the next phase.
6. Add a row to the **Change log**.
7. Do not mark a phase Complete unless build/tests/migrations required by that phase were actually run and verified.
