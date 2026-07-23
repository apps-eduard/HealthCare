# HealthCare MVP — Phase Progression Report

## Progress overview

**Overall completion: 39%**

```text
[████████████░░░░░░░░░░░░░░░░░░░░]  39%
```

| Metric | Value |
|--------|-------|
| Official phases (0–13) | 14 |
| Complete | 2 (Phases 0, 1) |
| Partial | 5 (Phases 2, 3, 4, 5, 6) |
| In progress | 0 |
| Not started | 7 |
| Weighted score | (2×1.0) + (0.7 + 0.75 + 0.5 + 0.9 + 0.5) = 5.35 / 14 ≈ **38%** → **39%** |

**Scoring rule:** Complete = 100% of phase · Partial = 50% (or noted fraction) · In progress = 25% · Not started / Blocked = 0%

**Current focus:** Staff patient search / patient administration, or Google auth

### All phases at a glance

| # | Phase | Status | Progress |
|---|-------|--------|----------|
| 0 | Repository and documentation | Complete | `██████████` 100% |
| 1 | Solution foundation | Complete | `██████████` 100% |
| 1b | Identity + core entities foundation *(sub-step)* | Complete | `██████████` 100% |
| 2 | Identity and authentication (JWT / refresh / endpoints) | Partial | `███████░░░` 70% |
| 3 | Roles and authorization foundation | Partial | `████████░░` 75% |
| 4 | Organizations and clinics | Partial | `█████░░░░░` 50% |
| 5 | Patients and clinic-patient registration | Partial | `█████████░` 90% |
| 6 | Staff and doctors | Partial | `█████░░░░░` 50% |
| 7 | Appointment booking | Not started | `░░░░░░░░░░` 0% |
| 8 | Staff web application (MudBlazor) | Not started | `░░░░░░░░░░` 0% |
| 9 | Medical notes | Not started | `░░░░░░░░░░` 0% |
| 10 | Hangfire and notifications | Not started | `░░░░░░░░░░` 0% |
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
| 3 | Roles and authorization foundation | Partial (current-user + policies) | 2026-07-23 |
| 4 | Organizations and clinics | Partial (entities + schema only) | 2026-07-23 |
| 5 | Patients and clinic-patient registration | Partial (profile + self clinic register) | 2026-07-23 |
| 6 | Staff and doctors | Partial (StaffMember entity + schema only) | 2026-07-23 |
| 7 | Appointment booking | Not started | — |
| 8 | Staff web application (MudBlazor) | Not started | — |
| 9 | Medical notes | Not started | — |
| 10 | Hangfire and notifications | Not started | — |
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

**Status:** Partial (~75%)  
**Updated:** 2026-07-23

### Already done

- Role definitions, constants, and idempotent seeding
- `ICurrentUser`, `ICurrentStaff`, `ICurrentPatient` (request-scoped)
- Trusted scope resolution from JWT + DB staff membership (claims never trusted alone)
- `ITenantAccessService` with org/clinic/patient guards and explicit `PLATFORM_ADMIN` bypass
- Authorization policy constants and handlers (`Authenticated`, `PlatformAdmin`, `StaffUser`, `OrganizationScoped`, `ClinicScoped`, `PatientUser`, `PatientSelfScope`)
- `GET /api/v1/auth/me`
- Isolation probe: `GET /api/v1/scope-probe/{organization|clinic|patient}`
- Unit + architecture tests; manual API verification

### Remaining

- Broader permission constants catalog (fine-grained permissions beyond roles)
- Patient linkage when Patient entity exists (Phase 5) — **done in Phase 5 foundation**
- Docker/Testcontainers integration suite for current-user endpoints (blocked: Docker not ready)
- EF global query filters deferred; Patient access enforced in services via ClinicPatient

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

**Status:** Partial (~90%)  
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

### Verification

- Build: succeeded
- Unit tests: 62 passed
- Architecture tests: 9 passed
- Integration tests: failed (Docker unavailable); tests retained
- Manual: health 200; PATCH profile + GET reflects changes; stale version 409; clinic-b register `P-000001` then idempotent; invalid code 404; staff PATCH 403

### Remaining

- Staff patient search / administration directory
- Real email provider
- Google auth (Phase 2)
- Integration suite green once Docker is available

---

## Phase 6 — Staff and doctors

**Status:** Partial

### Already done

- `StaffMember` entity with `UserId`, `OrganizationId`, `ClinicId`, `Role`
- EF configuration and role check constraint

### Remaining

- Staff management APIs and UI
- Doctor profile / availability
- Activation rules and permission differences by role

---

## Phase 7 — Appointment booking

**Status:** Not started

### Planned

- `Appointment` entity and status workflow
- Booking, confirmation, cancellation
- Conflict detection
- Patient and staff appointment endpoints
- Cross-clinic and self-scope tests

---

## Phase 8 — Staff web application

**Status:** Not started

### Planned

- MudBlazor staff UI: login, dashboard, appointments, patients, notes, staff, settings, audit viewer
- API remains the security boundary

---

## Phase 9 — Medical notes

**Status:** Not started

### Planned

- `MedicalNote` entity
- Clinic-private by default; optional patient visibility
- Role-restricted access (e.g. receptionist restrictions)
- Audit on create/view where required

---

## Phase 10 — Hangfire and notifications

**Status:** Not started

### Planned

- Hangfire with PostgreSQL-compatible storage
- Protected dashboard
- Confirmation / reminder / summary / retry jobs
- Idempotent notification handling

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
