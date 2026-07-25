# MVP Clinic Admin Scope

**Status:** Approved by product owner (2026-07-24). Authoritative for the `CLINIC_ADMIN` Web MVP.  
**Implementation:** Application code is **not** started yet. Permissions listed as approved are **not in the code matrix until their phase ships**.  
**Authority:** This document overrides informal notes in `Docs/security.md` §4.3 where they conflict; keep matrix and security cross-links in sync when coding.  
**Related:** `Docs/mvp-organization-admin-scope.md`, `Docs/authorization-matrix.md`, `Docs/security.md` §4.3.  
**Do not** copy Organization Admin capabilities into Clinic Admin without validating clinic scope.

Verified baseline when approved (2026-07-24):

- Organization Admin Web MVP complete
- Playwright Org Admin E2E complete
- Unit 336 · Architecture 19 · Web 220 · Integration 141 · E2E 9 · Total 725

---

## 1. Purpose

The **Clinic Admin** operates a **single clinic** inside one organization.

The Clinic Admin is responsible for:

- Day-to-day clinic operations
- Staff administration **inside their clinic**
- Patient directory and enrollment **for their clinic**
- Appointment queue and calendar **for their clinic**
- Doctor availability **for their clinic**
- Reminders and clinic appointment summaries **for their clinic**
- Clinic-scoped operational reporting (**approved**)
- Clinic profile maintenance for operational contact and scheduling defaults (**approved**)
- Clinic-filtered operational audit visibility (**approved**)
- Clinic dashboard with clinic operational counts only (**approved**)

The Clinic Admin must not:

- Administer another clinic
- Administer the organization as a whole
- Receive Platform Admin privileges
- Access medical-note content by admin role alone
- Change organization limits, billing, or clinic activation state
- See MaxClinics / MaxStaff / remaining organization capacity

---

## 2. Actor definition

```text
Actor: CLINIC_ADMIN
Membership: one active StaffMember bound to exactly one ClinicId + OrganizationId
Tenant key: ClinicId (membership) — never “first clinic” discovery
```

| Attribute | Rule |
|-----------|------|
| Role | `CLINIC_ADMIN` |
| Organization | Fixed to membership `OrganizationId` (read-only display) |
| Clinic | Fixed to membership `ClinicId` (no multi-clinic picker) |
| Working context | Membership clinic only; `IClinicWorkingContext` “All clinics” is **not** offered |
| Platform Admin | Using clinic APIs requires `platformAdminBypass=true` **and** explicit `clinicId` |

---

## 3. Security boundary

Every Clinic Admin operation must require:

```text
Required permission
+ Authenticated principal
+ Active staff membership (for CLINIC_ADMIN)
+ Clinic scope = membership ClinicId
+ Organization match = membership OrganizationId
```

Additional rules:

- Client-supplied clinic IDs that do not match membership are rejected (safe 404 / 403).
- **Never silently select the first clinic.**
- PLATFORM_ADMIN cross-tenant access requires explicit bypass **and** explicit target `ClinicId`.
- ORGANIZATION_ADMIN continues to use organization clinic-management routes for clinic CRUD/profile; they do **not** need `/api/v1/clinic/settings` for MVP (optional reuse of shared application services is fine).
- Permission grants capability only; resource access still requires tenant validation.
- UI gates are presentation only; API enforces authorization.
- BFF cookie/session flow unchanged; no browser-stored API tokens.
- **Patient** and **anonymous** actors are denied all Clinic Admin APIs and staff pages.
- `CLINIC_ADMIN` alone never receives `medical_notes.*`.

---

## 4. Organization and clinic isolation

```text
Organization (fixed)
  └── Clinic (fixed membership clinic)
        └── Staff / Patients / Appointments / Availability / Ops / Reports / Audit
```

| Allowed | Denied |
|---------|--------|
| Own clinic data | Other clinics in the same organization |
| Own organization display name (read) | Organization directory / org switcher |
| Clinic-scoped lists | “All clinics” aggregates / org totals |
| Assign clinic roles (see §10) | Assign `ORGANIZATION_ADMIN` / `PLATFORM_ADMIN` |

---

## 5. Explicit out-of-scope items

- Organization dashboard (`organization_dashboard.read`)
- Organization profile settings
- Organization security compromise-response console
- Organization-wide unfiltered audit logs
- Organization `/usage` page and MaxClinics / MaxStaff / remaining capacity
- Billing, subscriptions, plan changes, limit increases
- Organization suspend / delete
- Platform Admin features (`organizations.read` / `organizations.select` / Hangfire)
- Medical notes unless the user also holds a clinical role
- Clinic create / delete / activate / deactivate
- Staff change-clinic
- Cross-clinic patient enrollment
- Revenue / cross-clinic ranking / platform analytics
- Passwords, tokens, connection strings, raw provider payloads
- CSV export for clinic reports (deferred)
- Operating hours, branding/logo upload, public appointment instructions (deferred)

---

## 6. Permissions

### 6.1 Existing permissions granted to CLINIC_ADMIN (in code today)

```text
patients.read
patients.search
patients.update_clinic_status

appointments.read
appointments.create
appointments.confirm
appointments.cancel
appointments.check_in
appointments.complete
appointments.no_show
appointments.reschedule

availability.read
availability.manage_clinic

reminders.read
reminders.retry

summaries.read
summaries.retry

clinics.read

staff.read
staff.manage
staff.password_reset

roles.read
roles.assign

security_sessions.revoke
```

### 6.2 Existing permissions correctly withheld

```text
clinics.create / clinics.update / clinics.activate / clinics.deactivate / clinics.manage
organization_dashboard.read
organization_reports.read
organization_audit_logs.read
organization_usage.read
organization_profile.read / organization_profile.update
security_sessions.read
organizations.read / organizations.select
availability.manage_organization
medical_notes.*
hangfire.dashboard
```

### 6.3 Approved new permissions (add to catalog + matrix when implementing)

| Permission | Purpose | Phase |
|------------|---------|-------|
| `clinic_dashboard.read` | Clinic dashboard aggregates | CA-1 |
| `clinic_profile.read` | Read clinic settings | CA-2 |
| `clinic_profile.update` | Patch clinic-owned fields | CA-2 |
| `clinic_reports.read` | Clinic-scoped JSON reports | CA-8 |
| `clinic_audit_logs.read` | Clinic-filtered operational audit | CA-9 |

Do **not** create per-statistic permissions.  
Do **not** reuse `clinics.update` for Clinic Admin profile (slug/activation semantics).  
Do **not** reuse `organization_reports.read` / `organization_audit_logs.read` / `organization_dashboard.read`.

Also grant the new clinic permissions to **PLATFORM_ADMIN** for explicit-bypass support when those APIs ship. Do **not** grant them to DOCTOR / NURSE / RECEPTIONIST / PATIENT in MVP.

---

## 7. Navigation

Approved Clinic Admin staff-console menu (permission-gated):

```text
Dashboard

Appointments
  Appointment Queue
  Appointment Calendar

Patients
  Patient Directory

Scheduling
  Doctor Availability

Staff
  Staff Management
  Doctors
  Nurses
  Receptionists
  Clinic Admins   (same-clinic peers)

Operations
  Reminders
  Clinic Summaries
  Operations Health

Reports
  Clinic Reports

Clinic
  Clinic Profile
  Activity / Audit

Account
  Logout
```

Must **not** appear for Clinic Admin:

- Clinics create / activate / deactivate
- Organization Profile
- Usage & Limits (organization)
- Organization Security console
- Organization Audit (unfiltered)
- Organization Reports
- Platform tenant banner / org picker

---

## 8. Dashboard

### Approved API

```text
GET /api/v1/clinic/dashboard
Permission: clinic_dashboard.read
```

### Clinic-scoped cards / metrics (only)

- Active staff count
- Doctor count
- Patient count (clinic enrollment)
- Today appointment count (and by-status breakdown as useful)
- Monthly appointment count
- Pending enrollments (if available without new schema complexity)
- Failed reminders / operational warnings

### Must not return

- MaxClinics / MaxStaff / remaining capacity
- Billing / subscription
- Organization-wide totals
- Other clinics’ data
- Medical-note content

### UI

- Route: `/dashboard` shows **Clinic Dashboard** when `clinic_dashboard.read` and not org-dashboard-only path
- Clinic name + timezone visible; organization name read-only caption
- No clinic picker / “All clinics”
- Quick links only to permitted pages

### PLATFORM_ADMIN

Requires `platformAdminBypass=true` + explicit `clinicId` query. Never default clinic.

---

## 9. Clinic profile (Decision A — **approved**)

### Editable by CLINIC_ADMIN

| API / domain field | Maps to existing `Clinic` column |
|--------------------|----------------------------------|
| Name | `Name` |
| Specialty | `Specialty` |
| ContactEmail | `Email` |
| ContactPhone | `PhoneNumber` |
| Address | Prefer `AddressLine1` (+ `AddressLine2` if needed for continuity with org clinic update) |
| City | `City` |
| Country | `Country` |
| DefaultTimeZoneId | `TimeZoneId` |

Show a clear warning that timezone changes affect scheduling interpretation.

### Read-only

- OrganizationId, Organization name  
- Clinic Id  
- Slug / clinic code  
- Active/inactive status  
- CreatedAtUtc / UpdatedAtUtc  
- Subscription, limits, ownership  
- `Version` (concurrency token; client sends `ExpectedVersion`)

### Deferred

- Operating hours  
- Branding / logo upload  
- Public appointment instructions  

### Routes

```text
GET  /api/v1/clinic/settings
PATCH /api/v1/clinic/settings
```

### Permissions

- `clinic_profile.read`
- `clinic_profile.update`

### Clinic resolution

- **CLINIC_ADMIN:** membership `ClinicId` only (reject mismatched `clinicId`)  
- **PLATFORM_ADMIN:** explicit `clinicId` + `platformAdminBypass=true`  
- **ORGANIZATION_ADMIN:** continue using `/api/v1/organization/clinics/{id}`; both families should share application validation/update logic where practical  
- **Never** silently select the first clinic  

### Concurrency / audit

- Optimistic concurrency via `Version` / `ExpectedVersion`  
- Audit action: `clinic_profile_update`  
- 409 on conflict with reload guidance in UI  

---

## 10. Staff management

### Clinic Admin can

- List/search/create/update staff in **own clinic**
- Activate / deactivate (last Clinic Admin protected; no self-deactivate)
- Assign `CLINIC_ADMIN` / `DOCTOR` / `NURSE` / `RECEPTIONIST`
- Password reset; revoke sessions

### Forbidden

- Assign `ORGANIZATION_ADMIN` / `PLATFORM_ADMIN` / `PATIENT` as staff
- Change-clinic
- Manage other clinics’ staff

### Status

Backend **implemented**. Web `/staff` **implemented** and **CA-3 actor-aware hardened** (clinic picker/change-clinic hidden; clinic role filter).

---

## 11. Doctor directory

List doctors for membership clinic; use for appointments/availability. Backend + Web **implemented**.

### Status

**CA-4 delivered:** dedicated `/doctors` directory (staff `role=DOCTOR` when `staff.read`; booking directory fallback when only `availability.read`); clinic-scoped for Clinic Admin (no clinic picker); summary drawer with links to `/availability?doctorId=` and `/appointments?doctorId=`; activation remains on `/staff`.

---

## 12. Patient directory and enrollment

Search/detail/status/enroll **own clinic** only. Hide cross-clinic enroll UI for Clinic Admin (CA-5). Backend + Web base **implemented**.

### Status

**CA-5 delivered:** `/patients` is clinic-scoped for Clinic Admin (fixed clinic caption, no clinic picker, no cross-clinic Enroll UI). Status updates use `ExpectedVersion` (`Active`/`Inactive` only). Cross-clinic enrollment remains Org/Platform Admin. Staff patient APIs use `Authenticated` + service-enforced scope so Platform Admin requires explicit `clinicId` + `platformAdminBypass` (no silent first-clinic). No medical-note controls.

---

## 13. Appointment management

Queue/calendar + confirm/check-in/cancel/reschedule/complete/no-show. Backend + Web **implemented**. Org Admin remains without `appointments.complete`.

### Status

**CA-6 delivered:** `/appointments` (+ calendar) clinic-scoped for Clinic Admin (fixed clinic caption, no clinic picker); Complete visible when status allows (`CheckedIn`/`InProgress`); create dialog uses clinic caption (not disabled picker); staff appointment APIs use `Authenticated` + service scope so Platform Admin requires explicit `clinicId` + `platformAdminBypass`. Completing does not create medical notes.

---

## 14. Doctor availability

`availability.manage_clinic` for own clinic. Backend + Web **implemented**.

### Status

**CA-4 delivered:** Clinic Admin uses fixed clinic caption (no disabled clinic picker); deep-link `?doctorId=` from `/doctors`; weekly windows + exceptions + ExpectedVersion concurrency unchanged.

---

## 15. Reminders and operational summaries

Reminders, summary runs/retry, operations health — clinic scoped. Backend + Web **implemented** (CA-7 verify).

---

## 16. Clinic reports (Decision B — **approved**)

### Permission

`clinic_reports.read`

### Approved report content

1. Appointments by status  
2. Appointment volume by date  
3. Appointments by doctor  
4. Cancellation and no-show summary  
5. Patient enrollment summary  
6. Reminder and operational-health summary  

### Rules

- Max range **93 days**  
- Clinic scoped; aggregated only  
- **No** patient names, clinical content, billing, cross-clinic comparisons, organization totals  
- **JSON UI only** for MVP; **CSV export deferred**  

### Suggested routes (adjust to repo conventions; consolidated endpoint OK)

```text
GET /api/v1/clinic/reports/appointments
GET /api/v1/clinic/reports/doctors
GET /api/v1/clinic/reports/patients
GET /api/v1/clinic/reports/reminders
```

Web: `/clinic/reports` (CA-8).

---

## 17. Clinic audit (Decision C — **approved**)

### Permission

`clinic_audit_logs.read`

### Visible (clinic-filtered)

- Staff creation and status changes  
- Patient enrollment and status changes  
- Appointment actions  
- Availability changes  
- Reminder retries  
- Clinic profile updates  

### Must exclude

- Other clinics  
- Organization security events unrelated to the clinic  
- Platform administration  
- Billing  
- Tokens / credentials / password information  
- Raw metadata dumps  
- Medical-note contents  

### Features

Read-only list/detail/correlation. **No** edit, delete, export, or SIEM.

### Suggested routes

```text
GET /api/v1/clinic/audit-logs
GET /api/v1/clinic/audit-logs/{eventId}
GET /api/v1/clinic/audit-logs/by-correlation/{correlationId}
```

Web: `/clinic/audit-logs` (CA-9). May filter existing `OrganizationAuditEvents` by clinic.

---

## 18. Usage and limits (Decision D — **approved**)

Clinic Admin may see **only** clinic operational counts (primarily on the clinic dashboard):

- Active staff, doctors, patients  
- Today / monthly appointments  
- Pending enrollments  
- Failed reminders / operational warnings  

**Do not show:** MaxClinics, MaxStaff, remaining capacity, billing plan, subscription, organization `/usage`.

---

## 19. Error handling

Safe Problem Details mapping; no stack traces or secrets. 401 → sign-in; 403 → permission denied; out-of-scope → safe 404 where that pattern exists. Concurrency → reload guidance.

---

## 20. Concurrency

Clinic profile and existing staff/appointment/availability/patient updates use optimistic concurrency. UI disables save on conflict and offers reload.

---

## 21. Responsive / accessibility

Desktop/tablet first; critical flows usable on narrow widths. Accessible names/labels; loading/empty/error states; keyboard-reachable primary actions.

---

## 22. Backend requirements

Tenant enforcement via membership clinic; explicit PA bypass; approved permissions only when shipped; audited mutations; stable error codes; no Hangfire or medical-note grants via CLINIC_ADMIN alone.

---

## 23. Web requirements

BFF auth; nav gating; fixed clinic; reuse existing operational pages with actor-aware affordances; new pages for dashboard (enhance `/dashboard`), profile, reports, audit.

---

## 24–27. Test requirements

| Layer | Focus |
|-------|--------|
| Unit | Matrix grants; clinic dashboard/settings/reports/audit scope; hierarchy |
| Integration | Happy paths; cross-clinic denial; org endpoints 403; patient/anonymous denial; PA bypass rules; temp Postgres only |
| Web (bUnit) | Nav gates; dashboard cards; profile fields; hide change-clinic / cross-clinic enroll |
| E2E (docsvr) | Login → dashboard; profile/concurrency; staff/patients/appointments/ops smoke; reports/audit; org routes denied; cleanup |

---

## 28. Definition of Done (overall)

- [x] Scope approved by product owner  
- [ ] CA-1 … CA-10 complete per phase DoD  
- [ ] Unit + Architecture + Web + Integration + E2E green on required hosts  
- [ ] No org/platform privilege escalation  
- [ ] Cross-clinic, patient, and anonymous denial proven  
- [ ] Docs (matrix, security, phase-progress) updated when permissions ship  
- [ ] No secrets in logs/artifacts  

---

## 29. Explicit non-goals

Custom DB roles; multi-clinic Clinic Admin membership; billing; telemedicine; medical notes for CA; white-label CMS; operating-hours schema in MVP; mobile Clinic Admin app; CSV clinic reports in MVP.

---

## 30. Phase breakdown

| Phase | Theme | Complexity | Status |
|-------|--------|------------|--------|
| CA-1 | Nav + clinic dashboard API/UI | Medium | **Delivered** (2026-07-24) |
| CA-2 | Clinic profile settings | Medium | **Delivered** (2026-07-24) |
| CA-3 | Staff UI hardening | Small | **Delivered** (2026-07-24) |
| CA-4 | Doctor directory + availability verify | Small | **Delivered** (2026-07-25) |
| CA-5 | Patients verify | Small | **Delivered** (2026-07-25) |
| CA-6 | Appointments verify (Complete for CA) | Small | **Delivered** (2026-07-25) |
| CA-7 | Operations verify | Small | After CA-1 |
| CA-8 | Clinic reports (JSON) | Large | Approved |
| CA-9 | Clinic-filtered audit | Medium | Approved |
| CA-10 | Hardening + Playwright E2E | Medium | After prior phases |

### Phase DoD summaries

**CA-1:** `GET /api/v1/clinic/dashboard` + `clinic_dashboard.read`; Clinic Admin `/dashboard` shows clinic cards; org-only nav hidden; cross-clinic/PA/patient/anonymous tests green; **no migration**.  
**CA-2:** settings GET/PATCH; field allow-list; concurrency; `clinic_profile_update` audit; shared logic with org clinic update where practical.  
**CA-3–7:** Actor-aware UI on existing pages; no privilege leakage; Complete visible for CA.  
**CA-8:** Four report routes (or consolidated); 93-day cap; privacy rules; no CSV.  
**CA-9:** Clinic-filtered audit read APIs + UI; exclusions enforced.  
**CA-10:** Full smoke E2E on docsvr; DoD checklist complete.

---

# Cross-check: implementation status

| Capability | Intent | Permission | Backend | Web | Gap | Phase |
|------------|--------|------------|---------|-----|-----|-------|
| Clinic dashboard | Approved | `clinic_dashboard.read` | **Done** | **Done** (`/dashboard`) | — | CA-1 |
| Clinic profile | Approved | `clinic_profile.*` | **Done** | **Done** (`/clinic/settings`) | — | CA-2 |
| Staff ops | Matrix | existing staff.* | Done | **Done** (CA-3 actor-aware) | — | CA-3 |
| Doctors / availability | Matrix | availability.* + staff.read | Done | **Done** (`/doctors` + `/availability`) | — | CA-4 |
| Patients | Matrix | patients.* | Done | **Done** (CA-5 actor-aware) | — | CA-5 |
| Appointments | Matrix | appointments.* | Done | **Done** (CA-6 actor-aware + Complete) | — | CA-6 |
| Ops | Matrix | reminders/summaries | Done | Done | Verify | CA-7 |
| Clinic reports | Approved | `clinic_reports.read` | Missing | Missing | New | CA-8 |
| Clinic audit | Approved | `clinic_audit_logs.read` | Missing | Missing | New | CA-9 |
| Org limits /usage | Denied | — | CA denied | Gated | Intentional | — |
| Cross-clinic denial | Required | tenant | Done (existing) | Mostly | Keep for new APIs | all |
| Patient / anonymous denial | Required | auth | Done | Done | Keep | all |
| Medical notes | Denied for CA alone | none | Denied | N/A | Keep | — |

---

# Document history

| Date | Note |
|------|------|
| 2026-07-24 | Initial draft scope + decisions A–D (awaiting approval) |
| 2026-07-24 | **Approved** by product owner: profile fields, reports (JSON), clinic audit, usage counts-only, clinic dashboard endpoint, five new permissions |
| 2026-07-24 | **CA-1 delivered:** `clinic_dashboard.read`, `GET /api/v1/clinic/dashboard`, Clinic Dashboard UI, E2E smoke |
| 2026-07-24 | **CA-2 delivered:** `clinic_profile.read` / `clinic_profile.update`, `GET`/`PATCH /api/v1/clinic/settings`, Clinic Profile UI, E2E smoke |
| 2026-07-24 | **CA-3 delivered:** Clinic Admin actor-aware `/staff` (no clinic picker/change-clinic; clinic role filter); E2E staff smoke |
| 2026-07-25 | **CA-4 delivered:** `/doctors` directory + availability deep-link/actor polish; doctor DisplayName fix; Clinic Admin E2E doctors smoke |
| 2026-07-25 | **CA-5 delivered:** `/patients` actor-aware directory; hide cross-clinic enroll; status ExpectedVersion; Platform Admin Authenticated+bypass; E2E patients smoke |
| 2026-07-25 | **CA-6 delivered:** `/appointments` actor-aware queue/calendar; Complete for Clinic Admin; no-show E2E; staff appointment Authenticated+bypass |
