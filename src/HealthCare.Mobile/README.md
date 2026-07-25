# HealthCare.Mobile — Patient MAUI app

Android-first **.NET MAUI Blazor Hybrid** app for the Patient Mobile MVP.

**Status:** **PM-2 + PM-3 + PM-4 + PM-5 + PM-6 delivered.** PM-7 (security matrix) and PM-8 (Patient E2E) are **not** started.

Authoritative product scope: [`Docs/mvp-patient-scope.md`](../../Docs/mvp-patient-scope.md).

## Projects

| Project | Role |
|---------|------|
| `HealthCare.Mobile` | MAUI Blazor Hybrid Android app (`net10.0-android`) |
| `HealthCare.Mobile.Core` | Testable DI/config/API/auth/profile services (`net10.0`) |
| `HealthCare.Mobile.Tests` | Foundation + PM-3 unit tests |

Shared contracts only: `HealthCare.Contracts`. The mobile app does **not** reference Web, Application, or Infrastructure.

## Supported targets

- **Primary:** Android (`net10.0-android`, min API 24)
- **Not a release requirement yet:** iOS / Mac Catalyst / Windows (template folders may exist; TFM is Android-only)

## PM-3 authentication and profile

### Registration

- Screen: `/register` → `POST /api/v1/auth/register/patient`
- Fields: email, password, confirm password, first/last name, optional DOB and phone
- Client validation mirrors Application FluentValidation (password policy: 8+, upper/lower/digit/symbol)
- Success navigates to `/registration-complete` (confirmation required; no auto-login)
- Passwords are never logged

### Email confirmation

- Screen: `/confirm-email` → `POST /api/v1/auth/confirm-email` (+ resend via `POST /api/v1/auth/resend-confirmation`)
- Supports `?email=` / `?token=` query parameters when a link opens the app route
- **Known limitation:** OS-level deep-link / App Link registration is **not** configured in PM-3. Confirmation may complete in an external browser; the user then returns to the app and signs in. Manual token entry is supported.

### Login and Patient linkage

- Screen: `/sign-in` → `POST /api/v1/auth/login` then `GET /api/v1/auth/me`
- Tokens stored via `ISecureTokenStore`
- Requires PATIENT role, `HasLinkedPatient`, non-empty `PatientId`, and no active staff membership
- Unconfirmed email (`auth.email_not_confirmed`) and invalid credentials mapped to safe messages
- Linkage failure clears the session and blocks authenticated routes
- Does **not** trust JWT claims alone

### Session restoration

- Startup (`/`) runs `IPatientAuthenticationService.RestoreSessionAsync`
- Restores tokens, refreshes when access is expiring, resolves `/auth/me`, validates linkage
- Offline / network failure keeps tokens and shows offline UX (does not wipe a potentially valid session)
- Auth rejection clears the session

### Refresh and logout

- One refresh+retry on `401` (PM-2 handler); refresh endpoints are not intercepted recursively
- Logout: `POST /api/v1/auth/logout` with refresh token when possible, **always** clears local tokens, `forceLoad` navigate to sign-in

### Profile

- View: `/profile` → `GET /api/v1/patients/me`
- Edit: `/profile/edit` → `PATCH /api/v1/patients/me` with `ExpectedVersion`
- Editable: name fields, DOB, gender, mobile, preferred language, address, emergency contact
- Not shown: internal Patient ID, `LinkedUserId`, staff-only data
- `409` / `patient.concurrency_conflict`: conflict UX with reload-latest while preserving unsaved edits (no silent overwrite / auto-resubmit)

### Home

- Minimal Patient home with display name, profile prompt, Clinics entry, and My Appointments entry

### Navigation guards

- Authenticated: `/home`, `/profile`, `/profile/edit`, `/clinics` (+ details/doctors/availability/enroll), `/discovery/booking-next`, placeholder appointments
- Guest-only: sign-in, register, registration-complete, confirm-email (redirect to home when Patient-ready)
- `/connectivity` remains a diagnostic page

## PM-4 clinic and Doctor discovery

### Backend

| Route | Purpose |
|-------|---------|
| `GET /api/v1/patients/me/clinics` | Authenticated Patient directory (`search`, `specialty`, `page`, `pageSize`; max page size 50) |
| `GET /api/v1/patients/me/clinics/{clinicCode}` | Patient-safe clinic details |
| `POST /api/v1/patients/me/clinics/register` | Clinic-code enrollment (alternate path) |
| `GET /api/v1/clinics/{clinicCode}/doctors` | Doctors for active clinic (existing) |
| `GET /api/v1/clinics/{clinicCode}/doctors/{staffMemberId}/available-slots` | Available slots (existing) |

Staff directory `GET /api/v1/staff-management/clinics` remains inaccessible to Patients.

Search: trimmed, case-insensitive contains on name/city/address text; optional specialty contains on `Clinic.Specialty` string; active org + active clinic only; order by name then Id.

### Mobile screens

| Route | Screen |
|-------|--------|
| `/clinics` | Directory with search, specialty filter, paging, enrollment indicator |
| `/clinics/enroll` | Clinic-code enrollment |
| `/clinics/{code}` | Clinic details + enroll action |
| `/clinics/{code}/doctors` | Doctor list |
| `/clinics/{code}/doctors/{id}/availability` | Date + slots; select slot (not reserved) |
| `/discovery/booking-review` | Booking review + submit (`Requested`) |
| `/discovery/booking-success` | Success receipt (no resubmit); links to My Appointments |
| `/appointments` | My Appointments — Upcoming / Previous |
| `/appointments/{id}` | Appointment detail + cancel |
| `/appointments/{id}/reschedule` | Reschedule via same clinic/Doctor availability |

### Timezone

Prefer API clinic-local slot strings + `TimeZoneId`. Fallback: device-local conversion of UTC labeled “(device local)”. List, detail, cancel confirm, and reschedule review use the same display helpers.

### Discovery state

`IDiscoveryStateService` holds selected clinic/Doctor/date/slot in memory. Cleared when clinic/Doctor changes and on logout. Not a reservation until PM-5 submit succeeds. Reschedule reuses the same slot selection for the current appointment’s clinic/Doctor.

## PM-5 appointment booking

### Flow

1. Availability → Continue to booking review  
2. Review clinic/Doctor/slot/timezone  
3. Optional reason for visit (max 500)  
4. `POST /api/v1/patients/me/appointments` with clinic code, Doctor id, `AppointmentDateUtc` = slot `StartUtc`, duration  
5. Success screen: status **`Requested`**; clinic confirmation still required  

### Rules

- Enrollment required (checked before submit; backend authoritative)
- Slot conflict (`appointment.slot_conflict` / 409): clear slot, return to availability
- No application-level booking retry (timeout = uncertain outcome UX)
- Busy/disabled submit prevents double-tap
- Receipt store survives navigation without calling create again
- Logout clears discovery + receipt

## PM-6 My Appointments, cancellation, and rescheduling

### List

- `GET /api/v1/patients/me/appointments` (paged)
- **Upcoming:** non-terminal and end time ≥ now (nearest first)
- **Previous:** terminal **or** ended (most recent first)
- Loading / empty / offline / retry / load-more; session expiry → sign-in

### Detail

- `GET /api/v1/appointments/{id}` (own only; foreign → unavailable/`404`)
- Shows clinic, Doctor, time (+ timezone), status, reason, cancellation info when present
- Does **not** show medical notes, internal IDs, staff-only fields, or audit trail

### Cancel

- `POST /api/v1/appointments/{id}/cancel` with `ExpectedVersion`
- Eligible: `Requested` / `Confirmed` and ≥ 2 hours before start (exact 2 hours allowed)
- Confirmation: “Cancel appointment” / “This action cannot be undone”
- Inside cutoff: “Please contact the clinic”
- Success status: `CancelledByPatient`
- Concurrency `409`: reload; no auto-resubmit
- Timeout: uncertain outcome; reload to check; no automatic retry

### Reschedule

- Same clinic (from `ClinicSlug`); same Doctor preselected; fresh availability via PM-4 APIs
- Review current vs new time; confirm updates **the same appointment id**
- `POST /api/v1/appointments/{id}/reschedule` with `ExpectedVersion`
- Slot conflict: clear new slot and refresh availability
- Concurrency/cutoff/invalid status: reload appointment; no auto-resubmit

### Status-aware actions

| Status | Cancel / Reschedule |
|--------|---------------------|
| Requested / Confirmed | May show when outside 2h cutoff |
| CheckedIn / InProgress / terminal | Hidden |

Patient confirm, check-in, complete, no-show, and staff cancel are never exposed.

### Android runtime

PM-6 Android emulator/device smoke was **not** executed in this delivery. Android **build** + mobile automated tests are required.

## Configuration

Section: `Mobile` (`MobileAppOptions`).

| Setting | Purpose |
|---------|---------|
| `EnvironmentName` | Development / Emulator / Device / Staging / Production |
| `ApiBaseUrl` | Absolute API base |
| `HttpTimeoutSeconds` | 5–120 |
| `AllowCleartextHttp` | Dev/emulator HTTP only; **must be false in Production** |

### Android networking

| Scenario | Typical `ApiBaseUrl` |
|----------|----------------------|
| Android emulator → API on host | `http://10.0.2.2:5080` (cleartext, Dev only) |
| Physical device → host on LAN | `http://<host-lan-ip>:5080` (Dev) or HTTPS |
| Shared / docsvr | HTTPS base URL for that environment |
| Production | HTTPS only; cleartext forbidden |

- Cleartext allowlist: `10.0.2.2` / localhost / `127.0.0.1` only
- Overrides: `HEALTHCARE_API_BASE_URL`, `HEALTHCARE_MOBILE_ENV=Emulator`
- Do not disable TLS validation globally

## Restore / build / run

```powershell
dotnet workload install maui-android
dotnet restore HealthCare.sln
dotnet build src/HealthCare.Mobile/HealthCare.Mobile.csproj -f net10.0-android
dotnet test tests/HealthCare.Mobile.Tests/HealthCare.Mobile.Tests.csproj
```

Start the API, then deploy/run on an emulator or device. Seeded Patient accounts may be used for smoke sign-in.

## Known limitations

- Deep-link App Links for confirmation emails are not registered (manual token / browser confirm + return to app)
- Emulator runtime smoke may be unavailable in CI; Android **build** is required
- Google/OTP, notifications, Patient security matrix, Patient E2E: PM-7…PM-8
- No specialty catalog (clinic specialty string only); no maps/ratings/public photo subsystem
- `ClinicDoctorResponse` still includes `StaffMemberId` / `ClinicId` for API navigation; IDs are not shown in UI
- Reschedule keeps the same clinic and Doctor (backend clinic is fixed; UI does not offer Doctor change)
- No Blazor bUnit package in-repo; appointment UI logic covered via Core services + route/state tests
- Android runtime smoke: not verified unless emulator executed
