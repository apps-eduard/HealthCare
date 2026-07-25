# MVP Patient Scope

**Status:** **Approved** by product owner (2026-07-25). Authoritative for the **Patient Mobile MVP**.  
**Implementation:** **PM-1 delivered** (2026-07-25) — backend contract hardening. PM-2…PM-8 not started.  
**Authority:** This document overrides informal Patient notes in `Docs/security.md` §4.7 / §5.1, `Docs/architecture.md` §10.1 / §12.2, and `Docs/development-plan.md` Phase 11 where they conflict. Keep matrix and security cross-links in sync when coding.  
**Related:** `Docs/mvp-organization-admin-scope.md`, `Docs/mvp-clinic-admin-scope.md`, `Docs/mvp-doctor-scope.md`, `Docs/authorization-matrix.md`, `Docs/security.md` §4.7 / patient self-scope.  
**Do not** copy staff Web capabilities into the Patient app.  
**Do not** rebuild existing Patient APIs that already match this contract.  
**Do not** begin mobile UI (PM-2) until scheduled.

Verified baseline when approved (2026-07-25):

- Organization Admin Web MVP complete
- Clinic Admin Web MVP complete (CA-1–CA-10)
- Doctor Web MVP complete (DR-1–DR-7 + DR-9 + DR-10; DR-8 skipped)
- Patient API audit accepted: **partially complete**
- HEAD `c1e036a39f2e9c074f14078b5ee55210ff2d475d`
- Unit 511 · Architecture 19 · Web 327 · Integration 207 · Doctor E2E 8

---

## 1. Purpose

The **Patient** is a self-service actor with **one global Patient record** linked to exactly one authenticated user account.

The Patient is responsible for:

- Registering and authenticating with email and password
- Maintaining their own demographic and contact profile
- Discovering and enrolling with active clinics
- Discovering Doctors and available appointment slots
- Requesting appointments (status **`Requested`**)
- Viewing, cancelling, and rescheduling **their own** appointments under approved rules

The Patient must not:

- Access another patient’s data
- Access staff, clinic-admin, organization-admin, or platform endpoints
- View medical-note bodies, amendments, or clinical working content
- Confirm, check in, or mark arrival for appointments
- Manage Doctor availability or staff records
- Rely on mobile UI hiding as the security boundary

---

## 2. Personas

```text
Actor: PATIENT
Identity: ApplicationUser with role PATIENT + linked Patient row (UserId ↔ Patient.Id)
Tenant key for self-service: authenticated PatientId (server-resolved)
Clinic relationships: zero or more ClinicPatient enrollments (multi-clinic allowed)
Application surface: .NET MAUI Blazor Hybrid (Android first; iOS later)
```

| Attribute | Rule |
|-----------|------|
| Role | `PATIENT` |
| Patient record | Exactly one linked `Patient` per user |
| Organization / clinic | Not fixed membership; access via own enrollments and own appointments |
| Working context | Self-scope only; no staff “current clinic” picker |
| Platform Admin | Must not gain patient clinical content via Patient endpoints; staff bypass rules unchanged |
| Patient Web | **Not** in initial MVP (staff Blazor Web remains staff-only) |

---

## 3. MVP goals

1. Ship an Android-first Patient mobile app against the existing Patient API foundation.
2. Preserve implemented auth, profile, booking, list/detail, cancel, and reschedule behaviors unless this contract explicitly hardens them.
3. Add **authenticated clinic browse/search** so patients need not already know a clinic code.
4. Enforce safe concealment (`404`) for cross-patient resources, including foreign patient profile by ID.
5. Apply a **2-hour cancellation/reschedule cutoff** before appointment start.
6. Keep clinical notes and patient-visible clinical summaries **out** of this MVP.
7. Prove isolation with a Patient security matrix and mobile E2E pack (PM-7 / PM-8).

---

## 4. Explicit non-goals

| Out of scope | Notes |
|--------------|-------|
| Patient Web Blazor application | Staff Web only in this MVP |
| Google login | Deferred |
| Mobile OTP login | Deferred |
| Push / SMS / WhatsApp notifications | Deferred |
| In-app notification center | Deferred |
| Production email reminder delivery as Patient feature | Infrastructure may remain; not MVP completion criteria |
| Patient confirmation / self check-in / QR arrival | Staff-managed or future |
| Medical-note bodies / amendments / diagnoses UI | Deferred; `IsVisibleToPatient` not in this MVP |
| Insurance / national ID / photo / clinical document upload | Deferred unless already present (not present today) |
| Specialty subsystem redesign | Use existing optional `Clinic.Specialty` string only |
| Rebuilding working Patient booking/profile APIs | Align gaps only |
| Doctor / Clinic Admin / Org Admin feature changes | Unchanged |
| Dedicated staff `InProgress` API | Remains deferred from Doctor closeout |

---

## 5. Authentication

### Approved MVP

| Topic | Decision |
|-------|----------|
| Mechanism | Email + password via existing Identity + JWT |
| Registration | `POST /api/v1/auth/register/patient` remains enabled |
| Email confirmation | Required before login (existing rule) |
| Login | Shared `POST /api/v1/auth/login` |
| Tokens | Existing access JWT + refresh rotation |
| Logout | Existing `POST /api/v1/auth/logout` revokes refresh token |
| Password reset | May reuse existing Identity completion flow (`POST /api/v1/auth/complete-password-reset`) if wired for Patient accounts; **do not** invent a new custom reset system in PM-0 |
| Google | **Deferred** (future option) |
| OTP | **Deferred** (future option) |

### Current implementation

Shared login/refresh/logout, patient registration, and email confirmation are **implemented**. No Google/OTP endpoints exist. Staff-admin password-reset initiation is separate from Patient self-service.

---

## 6. Patient identity and account linkage

| Rule | Approved target | Current |
|------|-----------------|---------|
| One user ↔ one Patient | Required | Implemented (unique `Patients.UserId`; linker guards) |
| PatientId source | Server linkage / `ICurrentPatient`, never trusted client PatientId on self-service | Implemented for `/patients/me*` and patient booking |
| Unlinked PATIENT role | No effective Patient permissions; `PatientSelfScope` fails | Implemented (`PermissionService` strips PATIENT; policy fails) |
| Inactive user / inactive Patient | No effective Patient access | User inactive blocked at linker/auth; inactive patient rejected on booking |
| Staff + PATIENT mix | Forbidden | Implemented |
| Cross-org / cross-patient | Blocked | Appointments conceal with 404; foreign profile currently **403** → harden to **404** in PM-1 |

---

## 7. Patient profile

### Included

| Capability | Route (existing) | Notes |
|------------|------------------|-------|
| View own profile | `GET /api/v1/patients/me` | Requires `PatientSelfScope` |
| Update own profile | `PATCH /api/v1/patients/me` | Partial update + `ExpectedVersion` |

### Editable fields (aligned with current API)

- FirstName, MiddleName, LastName  
- DateOfBirth  
- Gender  
- MobileNumber  
- PreferredLanguage  
- Address  
- EmergencyContact  

### Immutable / non-patient-editable (response may include)

| Field | Rule |
|-------|------|
| `Id` | Read-only |
| `IsActive` | Staff/system managed |
| `LinkedUserId` | System managed |
| `Version` | Concurrency token only (not a demographic edit) |

### Deferred

Insurance, national ID documents, profile photo, clinical documents — **not implemented** and **not** in this MVP.

---

## 8. Clinic enrollment and discovery

### Clinic-code enrollment (retain)

- `POST /api/v1/patients/me/clinics/register` with public clinic code (`Clinic.Slug`)
- Alternate enrollment path; remains supported

### Authenticated clinic browse/search (**approved new MVP requirement**)

Patients must browse **active** clinics without already knowing a code.

| Filter | Approved |
|--------|----------|
| Clinic name | Yes |
| City / location text | Yes — `Clinic.City` (and related address text if already on the model) |
| Specialty | Yes — only as existing optional `Clinic.Specialty` string filter; **do not** invent a specialty catalog |

Rules:

- Only **active** organizations and **active** clinics appear  
- Authenticated Patient required for MVP browse (not anonymous public directory unless later approved)  
- Patient-safe DTOs only — no staff directory internals, org admin fields, or activation tooling  
- Existing staff route `GET /api/v1/staff-management/clinics` remains **staff-only** (patients already denied in service layer)

### Gap

Authenticated patient clinic browse/search API **missing** → **PM-4** (with contract review in PM-1).

---

## 9. Doctor discovery

### Included (existing)

- `GET /api/v1/clinics/{clinicCode}/doctors` → `ClinicDoctorResponse`  
  - Display name, specialty (clinic-level string), clinic code/id, accepts bookings, clinic timezone  

### Allowed

- List Doctors for an active clinic  
- Basic patient-display Doctor information as above  

### Denied

- Internal staff records / HR fields  
- Private Doctor contact (email/phone) unless a future approved patient-safe field is added  
- Availability CRUD / exception management  
- Staff-only doctor directory by clinic GUID without public code path (staff membership required today)

---

## 10. Availability and slot selection

### Included (existing)

- `GET /api/v1/clinics/{clinicCode}/doctors/{staffMemberId}/available-slots`  
- Patient selects Doctor + available slot for booking/reschedule  

### Denied

- Manage weekly availability or exceptions  
- View staff availability admin payloads beyond what slots endpoints return  

---

## 11. Appointment booking

### Authoritative current behavior (retain)

| Rule | Value |
|------|--------|
| Create | `POST /api/v1/patients/me/appointments` |
| Inputs | Clinic code, DoctorStaffMemberId, start UTC, duration, optional Reason / PatientNotes |
| PatientId | From authentication — **not** from client |
| Initial status | **`Requested`** (not auto-confirmed) |
| Source | `Patient` |
| Enrollment | Active `ClinicPatient` required (existing) |
| Inactive org/clinic/doctor/patient | Rejected |
| Overlap / slot conflict | **409** after authz |
| Concurrency | `ExpectedVersion` on later mutations |

**Do not** change booking to auto-confirm in this MVP.

---

## 12. My Appointments

### Included

| Capability | Existing API | Mobile UX |
|------------|--------------|-----------|
| List own appointments | `GET /api/v1/patients/me/appointments` | Upcoming + previous/terminal filters in UI |
| Detail | `GET /api/v1/appointments/{id}` (own only → else 404) | Detail screen |
| Display | Status, date/time, clinic/doctor display fields, reason, cancellation info where exposed | Required |

Patient sees **only** their own appointments.

### Shared `AppointmentResponse` decision (**PM-1**)

**Retained** shared `AppointmentResponse` for Patient and staff appointment routes (no breaking Patient-specific type in PM-1).

| Field | Patient-safe? | Rationale |
|-------|---------------|-----------|
| `Id` | Yes | Required |
| `OrganizationId`, `ClinicId`, `ClinicPatientId`, `PatientId` | Yes (own) | Own appointment scope; needed for multi-clinic client state; not other patients’ data |
| `DoctorStaffMemberId` | Yes | Required for reschedule |
| Times / duration / status / source / version | Yes | Required |
| `Reason`, `PatientNotes`, `CancellationReason` | Yes | Patient-supplied / cancellation info |
| `DoctorDisplayName`, `ClinicName`, `ClinicSlug`, `ClinicTimeZoneId` | Yes | Patient display |
| `PatientDisplayName`, `LocalPatientNumber` | Omitted for PATIENT actors | Staff queue helpers only |
| Medical notes | N/A | Never on this DTO |

Staff clients unchanged. Further DTO narrowing can wait for PM-7 if needed.

---

## 13. Cancellation

### Included

- `POST /api/v1/appointments/{id}/cancel` for own eligible appointments  
- Resulting status: **`CancelledByPatient`**  
- Optimistic concurrency via `ExpectedVersion`  

### Product cutoff (**PM-1 delivered**)

| Rule | Behavior |
|------|----------|
| Allowed when `startUtc - nowUtc >= 2 hours` | **Exactly 2 hours before start: allowed** |
| Denied when remaining &lt; 2 hours (including past starts) | `409` `appointment.patient_mutation_cutoff` |
| Staff cancel | **Unchanged** (no patient cutoff) |
| Out-of-scope appointment | `404` before version/cutoff disclosure |

Denial does not mutate state and does not emit a cancellation success audit.

---

## 14. Rescheduling

### Included (API already supports)

- `POST /api/v1/appointments/{id}/reschedule`  
- Own appointment only (else 404)  
- Eligible statuses: **`Requested`**, **`Confirmed`**  
- New slot must be valid/available; conflict → 409  
- `ExpectedVersion` required  
- Must not leave a duplicate active booking  
- Reschedule history preserves prior schedule (existing history row pattern)

### Product cutoff (**PM-1 delivered**)

Same **2-hour** rule as cancellation (`appointment.patient_mutation_cutoff` / `409`). Authz/ownership (`404`) precedes version, slot, and cutoff disclosure.

---

## 15. Clinical information restrictions

| Item | Patient MVP |
|------|-------------|
| Medical-note bodies | **Denied** |
| Amendments | **Denied** |
| Internal diagnoses / working notes | **Denied** |
| Doctor note lifecycle in Patient app | **Not exposed** |
| Patient-visible clinical summaries | **Deferred** (separate privacy decision) |
| `IsVisibleToPatient` | **Not** part of PM-0 or this MVP delivery |

Existing staff-only medical-note APIs remain authoritative.

---

## 16. Notifications

| Channel | Patient MVP |
|---------|-------------|
| In-app status via refreshed appointment APIs | **Yes** |
| Dedicated notification inbox | **No** |
| Push / SMS / WhatsApp | **Deferred** |
| Patient notification preferences | **Deferred** |
| Production email reminder delivery | **Deferred** as Patient feature |
| Existing Hangfire reminder jobs | May remain infrastructure-only; Dev sender / non-Dev NoOp is **not** a completed Patient notification feature |

---

## 17. Authorization and isolation

Every Patient self-service operation requires:

```text
Required permission
+ Authenticated principal
+ Effective PATIENT role (linked active Patient)
+ PatientSelfScope (or equivalent service ownership)
+ Resource PatientId == current PatientId (for patient-owned resources)
```

Additional rules:

- Never trust client-supplied PatientId for self-service identity  
- Never expand scope via client clinic/org IDs beyond public clinic-code resolution  
- Staff endpoints remain denied even if permission names overlap at catalog level (service denial + missing staff membership)  
- Medical notes remain staff-only  
- Platform Admin bypass must not unlock clinical note content through Patient surfaces  

---

## 18. Response semantics

| Code | Meaning |
|------|---------|
| `401` | Unauthenticated |
| `403` | Authenticated Patient lacks permission for a **non-concealed** surface (e.g. staff APIs, medical notes create) |
| `404` | Another patient’s appointment **or** concealed out-of-scope resource; **also** another patient’s profile by ID (**PM-1**) |
| `409` | Only after authorization + self-scope succeed — concurrency, overlap, slot conflict, invalid transition, **patient mutation cutoff** |

### Cross-patient profile concealment

| Surface | Behavior |
|---------|----------|
| `GET /api/v1/patients/{patientId}` as Patient for foreign/unknown id | **`404`** `authz.patient_not_found_or_denied` (**PM-1**) |
| Same endpoint as staff | Staff permissions + tenant scope (unchanged; typically `403` when out of clinic/org) |
| Foreign appointment | **404** |

---

## 19. Audit requirements

- Authorization denials for Patient cross-scope attempts must remain auditable without leaking clinical bodies  
- Appointment create / cancel / reschedule continue existing operational audit patterns  
- Do not log full profile dumps, note bodies, or raw tokens  
- Mobile clients must not persist medical data unnecessarily (none in this MVP)

---

## 20. Mobile UX scope

| Screen / flow | Milestone |
|---------------|-----------|
| App shell, DI, typed API client, secure token storage, env config | PM-2 |
| Register, confirm-email handling, login, refresh, logout, profile view/edit | PM-3 |
| Clinic browse/search, clinic-code enroll, Doctor list, slots | PM-4 |
| Book appointment + Requested confirmation UX | PM-5 |
| My Appointments, detail, cancel, reschedule (+ cutoff messaging) | PM-6 |
| Security denials (as app behavior + E2E) | PM-7 / PM-8 |

Platform: **.NET MAUI Blazor Hybrid**, Android first, iOS later. Existing `HealthCare.Mobile` placeholder becomes the Patient app in **PM-2**.

---

## 21. Accessibility and responsive expectations

- Primary target: phone-sized Android viewports  
- Touch-friendly controls; labeled form fields  
- Readable date/time in clinic timezone where API provides `ClinicTimeZoneId`  
- Full WCAG certification not required for MVP; basic accessible labels required  
- Narrow + normal device coverage in PM-8 where technically appropriate  

---

## 22. Testing requirements

| Layer | Focus |
|-------|--------|
| Unit | Cutoff rules; linkage; patient-safe mapping; browse filters |
| Integration HTTP | Profile 404 concealment; clinic browse; cancel/reschedule cutoff; booking regression |
| Mobile / UI | Auth, profile, discovery, book, my appointments (PM-3–PM-6) |
| Security matrix | PM-7 cross-patient/org/clinic; staff denial; notes denial; authz-before-409 |
| E2E | PM-8 full Patient journey on Android emulator or supported MAUI host |

Do not treat permission-catalog-only tests as sufficient for Patient product DoD.

---

## 23. Delivery phases

| Phase | Theme | Complexity | Status |
|-------|--------|------------|--------|
| **PM-0** | Patient MVP scope and contract | Docs | **Delivered** (2026-07-25) |
| **PM-1** | Patient contract and backend gap hardening | Medium | **Delivered** (2026-07-25) |
| **PM-2** | Patient mobile foundation (MAUI) | Medium | Not started |
| **PM-3** | Mobile authentication and profile | Medium | Not started |
| **PM-4** | Clinic and Doctor discovery | Medium | Not started |
| **PM-5** | Appointment booking (mobile) | Medium | Not started |
| **PM-6** | My Appointments, cancel, reschedule | Medium | Not started |
| **PM-7** | Patient security and negative matrix | Medium | Not started |
| **PM-8** | Patient mobile E2E | Medium | Not started |

### PM-1 — Patient contract and backend gap hardening — **delivered**

- Cross-patient profile → **404** (`authz.patient_not_found_or_denied`)
- Shared `AppointmentResponse` retained; `PatientDisplayName` / `LocalPatientNumber` omitted for PATIENT
- **2-hour** cancel/reschedule cutoff (`appointment.patient_mutation_cutoff` / 409); exact boundary allowed
- Authz-before-conflict coverage (foreign + stale version / bad slot → 404)
- Unit + HTTP integration suites extended
- **No mobile UI**

### PM-2 — Patient mobile foundation

- Convert/replace `HealthCare.Mobile` placeholder with MAUI Blazor Hybrid  
- Android-first configuration  
- DI, typed API client, environment config, secure token storage, navigation shell, shared errors  
- Connectivity/auth smoke only — **no booking UI yet**

### PM-3 — Mobile authentication and profile

- Register, confirm-email handling, login, refresh, logout  
- Current-user resolution  
- View/update Patient profile  

### PM-4 — Clinic and Doctor discovery

- Authenticated clinic browse/search API if still missing  
- Clinic-code enrollment  
- Doctor list + display profile + slots  
- Mobile discovery screens  

### PM-5 — Appointment booking

- Book → `Requested`  
- Reason for visit  
- Slot conflict / enrollment validation UX  

### PM-6 — My Appointments, cancellation, and rescheduling

- Upcoming/past/detail  
- Cancel + reschedule with 2-hour cutoff UX  
- Terminal-state action rules  
- Conflict refresh (`409`)  

### PM-7 — Patient security and negative matrix

- Cross-patient / org / clinic  
- Staff/admin denial  
- Medical-note denial  
- Authz before concurrency/workflow disclosure  
- Patient-safe DTO checks  

### PM-8 — Patient mobile E2E

- Register or seeded login → profile → discovery → book → detail → reschedule → cancel → denials  
- Android emulator or supported MAUI environment  
- Narrow + normal device coverage where appropriate  

---

## 24. Definition of Done (overall Patient MVP)

- [x] Scope approved by product owner (PM-0, 2026-07-25)  
- [x] PM-1 backend gap hardening complete  
- [ ] PM-2 … PM-8 complete  
- [x] Backend gaps closed for PM-1 (404 concealment, 2-hour cutoff, DTO review)  
- [ ] Android MAUI app supports full Patient MVP flow  
- [ ] Patient security matrix green  
- [ ] Patient E2E pack green on required host  
- [ ] Docs updated as phases ship  
- [ ] No secrets / note bodies in logs or mobile storage  

PM-0 alone does **not** complete the Patient MVP. PM-1 does **not** complete mobile.

---

## 25. Deferred items

| Item | Notes |
|------|--------|
| Google login | Future auth option |
| OTP login | Future auth option |
| iOS ship | After Android MVP |
| Patient Web app | Not in initial MVP |
| Push / SMS / WhatsApp / inbox | Deferred |
| Production reminder email as Patient feature | Deferred |
| Patient-visible clinical summaries / `IsVisibleToPatient` | Separate privacy decision |
| Insurance / ID / photo / clinical docs | Deferred |
| Patient confirm / check-in / QR | Deferred |
| Specialty catalog subsystem | Not invented; optional clinic string only |
| Full WCAG / load testing | Beyond MVP bar |

---

## 26. Product decisions (recorded)

| Topic | Decision |
|-------|----------|
| Application | .NET MAUI Blazor Hybrid; Android first; iOS later; no Patient Web in initial MVP |
| Auth | Email/password + self-registration + email confirm; JWT/refresh/logout existing; Google/OTP deferred |
| Identity | One user ↔ one Patient; server-resolved PatientId |
| Clinic discovery | Code enrollment retained + **authenticated browse/search** approved |
| Specialty filter | Only if using existing `Clinic.Specialty` string |
| Profile fields | Current editable set; insurance/photo/docs deferred |
| Booking status | Remain **`Requested`**; no auto-confirm |
| Cancel / reschedule cutoff | **2 hours** before start |
| Reschedule | **In MVP** (API already supports; add cutoff) |
| Confirm / check-in | **Out** of MVP |
| Clinical notes | **Out** of MVP |
| Notifications | API refresh only; no inbox/push/SMS for first release |
| Cross-patient profile | Target **404** |
| Appointment DTO | Review in PM-1; shared DTO interim acceptable if documented |

---

## Audit reconciliation table

| Capability | Existing implementation | Approved Patient MVP target | Gap | Planned milestone |
|------------|-------------------------|-----------------------------|-----|-------------------|
| Registration | `POST /auth/register/patient` + confirm | Keep | None | — (mobile UX PM-3) |
| Login | Shared `POST /auth/login` | Email/password MVP | Google/OTP deferred | PM-3 mobile |
| Account linkage | Unique UserId; linker; strip unlinked | Keep | None material | Verified PM-1 |
| Profile | `GET/PATCH /patients/me` | Keep fields + concurrency | None | PM-3 mobile |
| Clinic-code enrollment | `POST /patients/me/clinics/register` | Keep alternate path | None | PM-4 mobile |
| Clinic browse | Missing (staff directory denies Patient) | Authenticated browse/search | **API missing** | PM-4 |
| Doctor discovery | Doctors by clinic code | Keep patient-safe list | None material | PM-4 |
| Availability | Available-slots API | Keep | None material | PM-4 |
| Booking | Create → `Requested` | Keep; no auto-confirm | None (status) | PM-5 mobile |
| Appointment list/detail | Me list + get by id (own) | Keep | DTO review done PM-1 | PM-6 |
| Cancellation | Own + **2h cutoff** | Delivered | Mobile UX | PM-6 |
| Rescheduling | Own Requested/Confirmed + **2h cutoff** | Delivered | Mobile UX | PM-6 |
| Clinical visibility | Notes staff-only | Remain denied | None (intentional) | — |
| Notifications | Hangfire infra / NoOp prod | API refresh only | Not a Patient feature yet | Deferred |
| Cross-patient profile | **404** | **404** | Closed PM-1 | — |
| Mobile application | Class-library placeholder | MAUI Blazor Hybrid | **Not implemented** | PM-2…PM-6 |
| Patient E2E | Denial/seed only | Full Patient pack | **Missing** | PM-8 |

---

# Document history

| Date | Note |
|------|------|
| 2026-07-25 | **PM-0:** Initial authoritative Patient Mobile MVP scope from API audit + product-owner decisions |
| 2026-07-25 | **PM-1 delivered:** foreign patient profile `404`; 2-hour cancel/reschedule cutoff; AppointmentResponse retained with patient display scrub; authz-before-conflict tests |
