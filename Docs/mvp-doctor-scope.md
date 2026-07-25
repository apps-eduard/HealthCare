# MVP Doctor Scope

**Status:** **Approved** by product owner (2026-07-25). Authoritative for the `DOCTOR` Web MVP.  
**Implementation:** Doctor Web MVP **DR-1–DR-3 delivered** (2026-07-25). Later DR phases not started. Permissions ship with their phase.  
**Authority:** This document overrides informal notes in `Docs/security.md` §4.4 where they conflict; keep matrix and security cross-links in sync when coding.  
**Related:** `Docs/mvp-clinic-admin-scope.md`, `Docs/mvp-organization-admin-scope.md`, `Docs/authorization-matrix.md`, `Docs/security.md` §4.4 / medical-notes.  
**Do not** copy Clinic Admin or Organization Admin capabilities into Doctor without validating ownership and clinical least-privilege.  
**Do not** start Patient MVP or Mobile App from this document.

Verified baseline when approved (2026-07-25):

- Organization Admin Web MVP complete
- Clinic Admin Web MVP complete (CA-1–CA-10)
- HEAD `fdc40d6`
- Unit 430 · Architecture 19 · Web 296 · Integration 189 · E2E 22 · Total 956

---

## 1. Purpose

The **Doctor** is a clinical actor bound to **one active clinic membership**.

The Doctor is responsible for:

- Viewing and acting on **their own assigned** appointments
- Managing **their own** weekly availability and exceptions
- Accessing patient context under **appointment-linked** access (Model A)
- Creating and maintaining **appointment-linked medical notes** under tightened ownership (DR-6)
- Completing clinical appointment outcomes (complete / no-show) for **own** appointments
- Seeing a **doctor-scoped** operational dashboard (not clinic administration)

The Doctor must not:

- Administer the clinic or organization
- Browse the full clinic patient directory
- Mutate another doctor’s appointments
- Read or amend another doctor’s medical notes
- Manage other doctors’ availability
- Access clinic profile, clinic reports, clinic audit, staff management, org settings, or platform controls
- Rely on UI hiding as the security boundary

---

## 2. Actor definition

```text
Actor: DOCTOR
Membership: one active StaffMember bound to exactly one ClinicId + OrganizationId
Tenant key: ClinicId (membership) — never “first clinic” discovery
Clinical ownership key: StaffMemberId
  - Appointment.DoctorStaffMemberId == current StaffMemberId
  - MedicalNote.AuthorStaffMemberId == current StaffMemberId (mutations / approved reads)
```

| Attribute | Rule |
|-----------|------|
| Role | `DOCTOR` |
| Organization | Fixed to membership `OrganizationId` (read-only display) |
| Clinic | Fixed to membership `ClinicId` (no multi-clinic picker) |
| Working context | Membership clinic only; “All clinics” is **not** offered |
| Appointment ownership | **Required** for list/view/mutate |
| Patient access | **Model A** — appointment-linked (DR-5 tightens today’s clinic-wide APIs) |
| Platform Admin | Explicit org + clinicId + DoctorStaffMemberId + `platformAdminBypass=true`; **never** medical-note bodies |

---

## 3. Current implementation assessment vs approved target

### Critical security gaps (must close in named phases)

| Gap | Current behavior | Approved target | Phase |
|-----|------------------|-----------------|-------|
| Patient access | Clinic-wide enrollment (Model B) | Appointment-linked (Model A) | **DR-5 delivered** |
| Appointment ownership | Any clinic staff with permission may act | Doctor list/view/mutate **own** only | **DR-4 delivered** |
| Medical-note read | Any clinic Doctor/Nurse with permission | Author + authorized own appointment only | **DR-6** |
| Medical-note amend | Any clinic Doctor | Author-only append | **DR-6** |
| Doctor dashboard | Missing | `GET /api/v1/doctor/dashboard` | **DR-1** |
| Doctor profile | Admin staff APIs only | Self `GET/PATCH /api/v1/doctor/profile` | **DR-2 delivered** |
| Medical-notes Web | Missing | Appointment-detail entry | **DR-6** |
| Doctor E2E | None | Full pack | **DR-10** |

### Capability matrix (summary)

| # | Capability | Approved | Backend today | Web today | Phase |
|---|------------|----------|---------------|-----------|-------|
| 1 | Doctor dashboard | Yes | Missing | Generic `/dashboard` | DR-1 |
| 2 | Doctor profile | Yes | Delivered | Delivered | DR-2 |
| 3 | My clinic caption | Read-only | Partial | Partial | DR-1/DR-2 |
| 4–5 | Own availability + exceptions | Yes | Self-enforced | `/availability` self-locked | DR-3 delivered |
| 6–8 | Own schedule / list / detail | Yes | Own only (API + UI) | Shared pages (filter locked) | **DR-4** |
| 9–14 | Confirm/check-in/complete/no-show/cancel/reschedule | Own only | Own only | Shared | DR-4 / DR-7 |
| — | Appointment **create** | **Not in Doctor Web MVP** | Removed from DOCTOR matrix | No UI | **DR-4** |
| 15–18 | Patient summary/directory/history | Model A | Model A enforced | Shared | **DR-5** |
| 19–22 | Medical notes CRUD lifecycle | Tightened ownership | API broader than approved | Missing UI | **DR-6** |
| 23–26 | Diagnosis/treatment/Rx/docs | SOAP fields only; no Rx module | Notes only | — | DR-6 / out of scope |
| 27–29 | Ops summaries / audit browser / clinic reports | **No** | N/A or denied | Hide | — |
| 30–36 | Isolation / PA / CA / OA / patient / anon / inactive | Required | Partial | Partial | DR-9 |
| 37–40 | Concurrency / audit safety / a11y / responsive | Required | Partial | Partial | DR-7/DR-9/DR-10 |
| 41 | Doctor E2E | Required | — | None | DR-10 |

---

## 4. Security boundary

Every Doctor operation must require:

```text
Required permission
+ Authenticated principal
+ Active staff membership (role DOCTOR)
+ Clinic scope = membership ClinicId
+ Organization match = membership OrganizationId
+ Appointment ownership and/or patient-access rule and/or note authorship (as applicable)
```

Additional rules:

- Client-supplied clinic, patient, appointment, or doctor IDs never expand scope.
- DoctorStaffMemberId is always derived from authenticated membership (or explicit PA target), never trusted from the client for self-scope.
- **Never silently select the first clinic or first doctor.**
- UI gates are presentation only; API enforces authorization.
- Cross-patient / cross-appointment / cross-note → project-standard safe **404** (or existing safe denial) without confirming inaccessible existence.
- `CLINIC_ADMIN` / `ORGANIZATION_ADMIN` / `PLATFORM_ADMIN` alone never receive medical-note bodies.
- Platform Admin bypass **must never** unlock medical-note content.

---

## 5. Organization and clinic isolation

```text
Organization (fixed)
  └── Clinic (fixed membership clinic)
        └── Doctor StaffMember (self)
              └── Own appointments / own availability / Model A patients / own notes
```

| Allowed | Denied |
|---------|--------|
| Own assigned appointments | Other doctors’ appointments |
| Model A patients | Full clinic directory / unrelated search |
| Own notes under DR-6 rules | Peer doctors’ note bodies |
| Own availability | Peer / clinic-wide availability edit |
| Doctor dashboard metrics | Clinic/org reports, limits, billing |

---

## 6. Doctor ownership rules

| Resource | Rule |
|----------|------|
| Availability / exceptions | Target doctor = current StaffMemberId |
| Appointments list/view/mutate | `DoctorStaffMemberId` = current |
| Appointment create | **Not exposed** in Doctor Web MVP |
| Medical-note create | Appointment assigned to current doctor + eligible status |
| Medical-note read / draft update / sign / amend | Author = current **and** authorized appointment relationship |
| Doctor profile | Current StaffMember (+ Identity phone) only |

Covering-doctor, emergency break-glass, and Clinic Admin manual access grants are **deferred**.

---

## 7. Patient access decision — **approved Model A**

A DOCTOR may access a patient only when **all** are true:

1. Active Doctor staff membership  
2. Appointment belongs to the Doctor’s active clinic  
3. Appointment is assigned to that Doctor (`DoctorStaffMemberId` = current)  
4. Patient is the appointment’s patient  

| Scenario | Access |
|----------|--------|
| Upcoming assigned appointment | Allowed |
| Active assigned appointment (Confirmed/CheckedIn/InProgress) | Allowed |
| Completed assigned appointment | Historical access **allowed** |
| No-show assigned appointment | Historical access **allowed** |
| Cancelled appointment where Doctor was assigned before cancellation | Allowed |
| Another Doctor’s patient only | **Denied** (unless current Doctor also has an assigned appointment with that patient) |
| Full clinic patient directory | **Denied** |
| Unrelated patient search | **Denied** |
| Emergency / covering / CA manual grant | **Deferred** |

Cross-patient access → safe denied/not-found (no existence leak).

**Current APIs enforce Model A for Doctor (DR-5).** Clinic Admin remains clinic-wide enrollment search.

---

## 8. Appointment access and actions — **approved**

### Statuses

`Requested`, `Confirmed`, `CheckedIn`, `InProgress`, `Completed`, `CancelledByPatient`, `CancelledByClinic`, `NoShow`

### Doctor rules

- List / view / mutate **only** appointments assigned to authenticated `DoctorStaffMemberId`
- Reject client attempts to select another Doctor
- Clinic Admin remains clinic-wide

### Approved actions on **own** appointments (domain transitions apply)

| Action | Doctor MVP |
|--------|------------|
| Confirm | Yes |
| Check-in | Yes |
| Complete | Yes — **does not** auto-create a note; note **not** required for completion |
| No-show | Yes |
| Cancel | Yes |
| Reschedule | Yes |
| Create | **No** Web MVP surface |

### `appointments.create` permission

DOCTOR **no longer** holds `appointments.create` in `RolePermissionMatrix` (removed in DR-4). Doctor Web MVP does not expose create UI; Clinic Admin / Organization Admin / Receptionist retain create.

`ExpectedVersion` remains required for status mutations and reschedule.

`InProgress` transition API remains deferred unless a later assessment requires it.

---

## 9. Availability — **approved**

Doctor may manage only:

- Own weekly availability  
- Own full-day exceptions  
- Own time-range exceptions  
- Own available slots / schedule  

Doctor may not: select/edit another doctor; manage clinic-wide availability; override booked appointments; change clinic timezone.

Clinic Admin keeps clinic-wide availability management.

**Web:** reuse `/availability` (already self-scoped for Doctor).

---

## 10. Doctor profile — **approved**

### Editable (self)

| Field | Storage note |
|-------|----------------|
| DisplayName | `StaffMember.DisplayName` |
| FirstName | `StaffMember.FirstName` |
| LastName | `StaffMember.LastName` |
| JobTitle | `StaffMember.JobTitle` |
| ContactPhone | Identity `ApplicationUser.PhoneNumber` (same as staff admin phone) |

### Specialty

`Specialty` exists on **`Clinic`**, not `StaffMember`. Doctor self-management is **not** the authoritative ownership model → **Clinic Admin-controlled**; not Doctor-editable in MVP.

### Read-only

StaffMemberId, Email/login, Role, Clinic, Organization, Active status, Version, CreatedAtUtc, UpdatedAtUtc.  
Staff code / AcceptsBookings / license fields: not Doctor-editable (expose read-only only if already present on responses; otherwise omit).

### Deferred

Biography, profile photo, personal timezone, credential/licensing workflows.

### Routes and permissions

```text
GET   /api/v1/doctor/profile
PATCH /api/v1/doctor/profile
```

- `doctor_profile.read`
- `doctor_profile.update`

Doctor ID from authenticated membership. `ExpectedVersion` required. Safe audit metadata only.  
Grant to DOCTOR + PLATFORM_ADMIN (explicit bypass troubleshooting only).

Web: `/doctor/profile`

---

## 11. Doctor dashboard — **approved**

```text
GET /api/v1/doctor/dashboard
Permission: doctor_dashboard.read
```

### Content (assigned-doctor scope only)

- Today’s assigned appointments  
- Upcoming assigned appointments  
- Checked-in appointments  
- Appointments awaiting completion  
- Recent no-show count  
- Next appointment  
- Own availability warnings  
- Draft-note count (**after DR-6 ships**; omit or zero until then)

### Exclude

Clinic-wide staff/patient counts; other doctors’ performance; org limits; billing; subscription; clinic/org reports.

Web: `/dashboard` shows **Doctor Dashboard** when `doctor_dashboard.read` (actor-aware, same pattern as Clinic Admin).

Grant `doctor_dashboard.read` to DOCTOR + PLATFORM_ADMIN (explicit clinic + doctor context for PA).

---

## 12. Schedule

- `/appointments/calendar` and `/appointments` with Doctor locked to self (no peer doctor picker).  
- No vanity `/my-schedule` required if actor-aware reuse is clear.  
- Timezone: clinic IANA via existing converters.

---

## 13. Patient summary

Under Model A (after DR-5):

- Directory = authorized patients only  
- Detail = care demographics from existing staff patient contracts (non-clinical)  
- Appointment history = encounters involving the current doctor  
- Note bodies only via authorized medical-note routes (DR-6)

---

## 14. Medical-note decision — **approved for DR-6 (tightened)**

### Current vs approved

| Behavior | Current API | Approved |
|----------|-------------|----------|
| Create | Clinic Doctor on eligible appointment | Own assigned appointment only |
| Read | Any clinic Doctor/Nurse with permission | **Author** notes for authorized appointments only |
| Draft update / sign | Author-only | Keep author-only |
| Amend | Any clinic Doctor | **Author-only** append |
| Delete | Not supported | Keep none |
| CA / OA / Patient / PA bodies | Denied | Keep denied; PA bypass never unlocks notes |

### Lifecycle

- Draft: editable by author (`ExpectedVersion`)  
- Signed: immutable  
- Amendment: append-only new signed row + reason  
- No overwrite of finalized content  
- No delete  

Permission checks alone are insufficient — enforce **author ownership + appointment/patient relationship**.

Audit metadata must never include note bodies, diagnosis/assessment text, treatment/plan text, or prescription details.

Web: enter from **appointment detail** (no global Medical Notes nav unless later assessment proves necessary). Optional deep link `/medical-notes/{id}`.

---

## 15. Reports — **approved: none**

No Doctor report center. Dashboard summaries only.  
**DR-8 skipped by default.** No exports or comparative analytics.

Reminders/summaries permissions may remain on DOCTOR in the matrix but are **out of Doctor Web MVP navigation**.

---

## 16. Audit visibility — **approved: no browser**

No `/audit-logs` or `/clinic/audit-logs` for Doctor.  
Domain history may appear in appointment detail, medical-note lifecycle, and availability confirmations.

---

## 17. Navigation — **approved**

```text
Dashboard
Appointments
Patients
Availability
My Profile
Logout
```

Medical notes: from appointment detail (DR-6).

**Must not show:** Staff, Doctors directory, Clinic profile/reports/audit, Operations administration, Organization settings/security/usage, Platform controls.

---

## 18. Permissions

### Current Doctor grants (code today)

```text
patients.read / patients.search
appointments.* (including create)
availability.read / availability.manage_self
reminders.read / reminders.retry
summaries.read
clinics.read
medical_notes.read / create / update_draft / sign / amend
```

### Approved new permissions (ship with phase)

| Permission | Phase |
|------------|-------|
| `doctor_dashboard.read` | DR-1 |
| `doctor_profile.read` | DR-2 |
| `doctor_profile.update` | DR-2 |

### Must not receive

`organization_*`, `clinic_dashboard.*`, `clinic_profile.*`, `clinic_reports.*`, `clinic_audit_logs.*`, `staff.*`, `roles.assign`, Hangfire, billing/subscription, clinic lifecycle.

### Classification notes

| Item | Classification |
|------|----------------|
| Appointment ownership missing | **Mitigated in DR-4** |
| `appointments.create` on DOCTOR | **Removed in DR-4** |
| Clinic-wide patient access | **Mitigated in DR-5 (Model A)** |
| Clinic-wide note read/amend | **Mitigated in DR-6 (author + own appointment)** |
| reminders/summaries | Existing; **out of Doctor nav** |

---

## 19–24. Backend / Web / errors / concurrency / audit / a11y

Same standards as Clinic Admin MVP: membership resolution, Problem Details, `ExpectedVersion` on profile/availability/appointments/notes, safe audits, responsive/accessible staff console patterns.

Platform Admin troubleshooting (non-note APIs):

```text
platformAdminBypass=true
+ explicit organization context (where architecture requires)
+ explicit clinicId
+ explicit DoctorStaffMemberId
```

No silent first-doctor selection. No Doctor impersonation feature in MVP.

---

## 25–28. Test requirements

| Layer | Focus |
|-------|--------|
| Unit | New permissions; dashboard aggregates; ownership; Model A; note author rules |
| Integration | Happy paths; cross-doctor/cross-clinic/cross-patient; CA/OA/Patient/anon; PA bypass limits; notes non-bypassable |
| Web | Nav gates; own-only filters; profile fields; note UI absent for CA |
| E2E | Doctor login pack through DR-10 |

---

## 29. Explicit non-goals

Patient MVP; mobile; Nurse console; prescriptions/imaging/labs vault; telemedicine; covering/emergency access; multi-clinic Doctor; Doctor reports/export; note-required-before-complete; hard delete notes; PA note bodies; billing/Hangfire for Doctor; Doctor appointment create UI.

### Post-MVP backlog

Covering-doctor / care-team; InProgress API; note-before-complete policy; specialty/bio/photo on staff; full WCAG/load tests; Nurse Web MVP; patient-visible note summaries.

---

## 30. Definition of Done (overall)

- [x] Scope approved by product owner (2026-07-25)  
- [ ] DR-1 … DR-10 complete (DR-8 skipped)  
- [ ] Unit + Architecture + Web + Integration + E2E green on required hosts  
- [ ] Appointment ownership enforced for Doctor  
- [ ] Patient access = Model A  
- [ ] Medical-note Web + tightened ownership green  
- [ ] No org/clinic-admin privilege leakage  
- [ ] Docs updated as permissions ship  
- [ ] No secrets / note bodies in logs or artifacts  

---

## 31. Phase roadmap — **final**

| Phase | Theme | Complexity | Status |
|-------|--------|------------|--------|
| DR-1 | Doctor dashboard + navigation foundation | Medium | **Delivered** (2026-07-25) |
| DR-2 | Doctor profile | Small | **Delivered** (2026-07-25) |
| DR-3 | My availability and schedule | Small | **Delivered** (2026-07-25) |
| DR-4 | My appointment ownership and workflows | Medium | **Delivered** (2026-07-25) |
| DR-5 | Appointment-linked patient access | Large | **Delivered** (2026-07-25) |
| DR-6 | Medical notes ownership and lifecycle | Large | **Delivered** (2026-07-25) |
| DR-7 | Clinical workflow and completion hardening | Medium | **Delivered** (2026-07-25) |
| DR-8 | Doctor reports | — | **Skipped by default** |
| DR-9 | Cross-role security, audit, negative testing | Medium | **Delivered** (2026-07-25) |
| DR-10 | E2E hardening and Doctor MVP completion | Medium | Not started |

### DR-1 — Doctor dashboard and navigation foundation

- **Objective:** Doctor-scoped dashboard API + actor-aware `/dashboard` + Doctor nav without admin leakage  
- **Existing backend reused:** membership/`ICurrentStaff`, clinic timezone helpers, appointment aggregates patterns from clinic dashboard  
- **Backend work:** `doctor_dashboard.read`; `GET /api/v1/doctor/dashboard`; PA requires clinicId + DoctorStaffMemberId + bypass  
- **Web work:** Doctor dashboard view; StaffLayout Doctor nav; preserve OA/CA dashboards  
- **Permissions:** `doctor_dashboard.read` → DOCTOR + PLATFORM_ADMIN  
- **Security tests:** cross-clinic denial; no clinic-wide counts; PA missing context → 400/403; Patient/anon denied  
- **Unit / Integration / Web / E2E:** aggregates; authz matrix; nav gates; one Doctor login→dashboard smoke  
- **Dependencies:** none  
- **Complexity:** Medium  
- **DoD:** Doctor sees only assigned-appointment metrics + availability warnings; CA/OA/platform nav hidden; tests green; **no migration**  

### DR-2 — Doctor profile

- **Objective:** Self-edit DisplayName/FirstName/LastName/JobTitle/ContactPhone  
- **Reuse:** StaffMember + Identity phone patterns; concurrency  
- **Backend:** `GET/PATCH /api/v1/doctor/profile`; `doctor_profile.read/update`  
- **Web:** `/doctor/profile`  
- **Security tests:** cannot change role/clinic/email/active; ExpectedVersion 409  
- **Deps:** DR-1 nav  
- **Complexity:** Small  
- **DoD:** Persistence + audit metadata; Specialty not editable  

### DR-3 — My availability and schedule

- **Objective:** Confirm self-only availability UX; calendar/queue defaults to self  
- **Reuse:** `/availability`, availability APIs, calendar page  
- **Backend:** minor defaults only if needed  
- **Web:** lock doctor picker to self for Doctor  
- **Security tests:** cannot edit peer availability  
- **Deps:** DR-1  
- **Complexity:** Small  

### DR-4 — My appointment ownership and workflows

- **Objective:** API + UI ownership for list/view/mutate; no create UI; assess `appointments.create` on DOCTOR  
- **Reuse:** AppointmentsController / AppointmentService  
- **Backend:** enforce `DoctorStaffMemberId` for Doctor actors  
- **Web:** `/appointments` own-only actions (confirm/check-in/complete/no-show/cancel/reschedule)  
- **Security tests:** sibling doctor appointment denied; CA still clinic-wide  
- **Deps:** DR-1  
- **Complexity:** Medium  

### DR-5 — Appointment-linked patient access

- **Objective:** Replace clinic-wide Doctor patient APIs with Model A  
- **Reuse:** StaffPatients APIs  
- **Backend:** filter search/detail by assigned-appointment relationship (including historical completed/no-show/cancelled-as-assigned)  
- **Web:** `/patients` authorized set only  
- **Security tests:** unrelated patient 404; peer-only patient denied; no directory dump  
- **Deps:** DR-4  
- **Complexity:** Large  

### DR-6 — Medical notes ownership and lifecycle

- **Objective:** Web UI + tighten read/amend to author + own appointment; create only on own appointments  
- **Reuse:** MedicalNotesController / MedicalNoteService  
- **Backend:** ownership harden; WebPermissions + typed client  
- **Web:** appointment-detail note UX; optional `/medical-notes/{id}`  
- **Security tests:** peer note denied; CA/OA/Patient/PA denied bodies; audit has no SOAP text; concurrency  
- **Deps:** DR-4, DR-5  
- **Complexity:** Large  

### DR-7 — Clinical workflow and appointment completion hardening

- **Objective:** Polish own-appointment transitions; 409 reload; completion without mandatory note  
- **Reuse:** appointment mutations  
- **Web:** safer Doctor action confirmations  
- **Tests:** invalid transitions; double-submit  
- **Deps:** DR-4; DR-6 optional for draft-note prompts  
- **Complexity:** Medium  

### DR-8 — Doctor reports

- **Status:** **Skipped by default** — no implementation unless a later approved product need appears.

### DR-9 — Cross-role security, audit and negative testing

- **Objective:** Full negative matrix; PA bypass limits; inactive membership; note non-bypass  
- **Status:** **Delivered** (2026-07-25) — unit permission/ownership matrix + integration HTTP matrix + Web gate tests  
- **Deps:** DR-1–DR-7  
- **Complexity:** Medium  

### DR-10 — E2E hardening and Doctor MVP completion

- **Objective:** Playwright Doctor pack on docsvr; mark MVP complete in docs  
- **Scenarios:** login/logout; dashboard; profile; availability; own appointment action; Model A patient; note draft/sign; admin routes denied; narrow viewport  
- **Deps:** prior phases  
- **Complexity:** Medium  

---

## 32. Product-owner decisions (recorded)

| Topic | Decision |
|-------|----------|
| Patient access | **Model A** approved |
| Appointment ownership | Own assigned only for Doctor |
| Appointment create UI | Not in Doctor Web MVP |
| Completion vs notes | No auto-note; note not required to complete |
| Medical notes | DR-6 approved with author + own-appointment ownership |
| Availability | Self-only (keep) |
| Profile fields | DisplayName, First/Last, JobTitle, ContactPhone; Specialty CA-controlled |
| Dashboard | Approved endpoint + metrics |
| Reports | None; DR-8 skipped |
| Audit browser | None |
| Platform Admin | Explicit org/clinic/doctor + bypass; never note bodies |
| Navigation | Dashboard, Appointments, Patients, Availability, My Profile |

---

# Document history

| Date | Note |
|------|------|
| 2026-07-25 | Initial draft from repository inspection |
| 2026-07-25 | **Approved** by product owner: Model A patients; appointment ownership; medical-note tighten; profile/dashboard permissions; DR-8 skipped; no Doctor create UI |
| 2026-07-25 | **DR-1 delivered:** `doctor_dashboard.read`, `GET /api/v1/doctor/dashboard`, Doctor Dashboard UI + Doctor console nav (Patients hidden until DR-5) |
| 2026-07-25 | **DR-2 delivered:** `doctor_profile.read` / `doctor_profile.update`, `GET/PATCH /api/v1/doctor/profile`, `/doctor/profile` My Profile UI; Specialty remains Clinic Admin–controlled |
| 2026-07-25 | **DR-3 delivered:** confirm self-only `/availability`; lock queue/calendar doctor filter to self; peer availability edit denied |
| 2026-07-25 | **DR-4 delivered:** Doctor appointment ownership (list/view/mutate); removed `appointments.create` from DOCTOR; sibling peer denial; CA clinic-wide unchanged |
| 2026-07-25 | **DR-5 delivered:** Model A patient access (appointment-linked search/detail); Patients nav restored for Doctor; peer-only/unrelated denial |
| 2026-07-25 | **DR-6 delivered:** author + own-appointment medical notes; appointment-detail note UX; peer note denied (404); WebPermissions + typed client |
| 2026-07-25 | **DR-7 delivered:** transition/completion hardening; completion without mandatory note; safer confirmations; denial audit; Cancel aligned for CheckedIn/InProgress |
| 2026-07-25 | **DR-9 delivered:** cross-role authorization negative matrix (unit + integration HTTP); Doctor report/audit surfaces remain denied; PA note non-bypass reconfirmed |
