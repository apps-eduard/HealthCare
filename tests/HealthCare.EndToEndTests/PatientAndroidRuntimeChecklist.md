# Patient Android Runtime Acceptance Checklist (PM-8 Layer B)

This checklist is the **manual** half of the Patient Mobile E2E pack when native UI automation
(Appium / MAUI UITest / UI Automator) is **not** available in the repository or host environment.

**Do not** treat Layer A (API journey tests in `PatientMobileMvpE2eTests`) as full mobile E2E.
**Do not** claim Playwright Web Doctor/Clinic suites prove the MAUI Patient app.

## When Layer B is required

Mark PM-8 **fully complete** only after this checklist (or equivalent Appium session) succeeds on a
real Android emulator or physical device against a running API.

If no emulator/device session is possible, record:

- Layer A: complete (automated API pack green on docsvr)
- Layer B: **pending**
- Exact blocker (example: no AVD / no system images / no Appium packages)

## Environment (fill in at runtime)

| Item | Value |
|------|--------|
| Host OS | |
| Android SDK / cmdline-tools | |
| Emulator AVD or device model | |
| Android API level | |
| App configuration | `Debug` / Android |
| Backend base URL | (dev API reachable from emulator, typically `10.0.2.2:{port}` or LAN) |
| Database | Development / ephemeral test host |
| Email confirmation | External browser **or** API/dev confirmation-token helper |
| Seeded accounts | Synthetic only (`*.healthcare.local`) |
| Screenshots / video | Failures only; gitignored; no credentials |

## Preflight

- [ ] Android emulator or physical device connected (`adb devices` shows device)
- [ ] `HealthCare.Mobile` Android target builds and deploys
- [ ] App launches to splash/startup without crash
- [ ] Backend health reachable from the device/emulator
- [ ] Synthetic Patient accounts prepared (confirmed + unconfirmed if testing registration UX)

## Scenario checklist

### 1 — Startup and anonymous navigation

- [ ] App launches; startup loading appears then clears
- [ ] Anonymous session lands on sign-in / welcome
- [ ] Registration is reachable
- [ ] Connectivity diagnostic only if approved for this build
- [ ] Protected tabs/routes redirect to sign-in when anonymous
- [ ] No staff/admin navigation visible

### 2 — Registration and confirmation-required UX

- [ ] Submit valid synthetic registration
- [ ] App shows confirmation-required status (no auto-login)
- [ ] Return to sign-in works
- [ ] Confirmation completed via external browser **or** documented API/helper (not App Links)
- [ ] App Links / native deep-link confirmation still **deferred** (not claimed)

### 3 — Login and linkage

- [ ] Confirmed linked Patient signs in; home appears after `/auth/me` linkage validation
- [ ] Safe display fields only (no internal Patient GUID, no staff fields)
- [ ] Unlinked Patient-role account cannot enter Patient home (safe denial)

### 4 — Session restoration

- [ ] After force-stop / relaunch, session restores from secure storage when valid
- [ ] Invalid/revoked tokens return to sign-in

### 5 — Profile

- [ ] Profile loads approved demographics
- [ ] Edit + save + reload persists
- [ ] No clinical / staff / internal-ID leakage

### 6–8 — Clinic / Doctor / availability

- [ ] Browse/search clinics; inactive absent; safe fields only
- [ ] Clinic-code enrollment succeeds or already-enrolled safely; invalid code reveals nothing sensitive
- [ ] Doctors list + future slot selection; no staff contact / membership leakage
- [ ] Selected slot not shown as reserved before booking

### 9–10 — Booking and My Appointments

- [ ] Review → submit once → `Requested` + pending clinic confirmation copy
- [ ] Rapid re-tap does not create a duplicate
- [ ] New appointment appears in My Appointments; detail shows clinic/Doctor/time/timezone/status/reason
- [ ] No medical note / audit / staff-only data

### 11–12 — Cancel and reschedule

- [ ] Outside two-hour cutoff: cancel → `CancelledByPatient`; action gone
- [ ] Inside cutoff: cannot cancel; approved “contact the clinic” behavior
- [ ] Reschedule eligible appointment → same identity, new schedule, no second appointment

### 13–15 — Terminal, restricted, concealment

- [ ] Terminal/progressed statuses hide Patient cancel/reschedule and all staff actions
- [ ] No medical notes / reports / audit / staff / org / clinic admin / Doctor workflow surfaces
- [ ] Cross-patient appointment detail/cancel/reschedule unavailable (404-style UX; no existence-confirming conflict)

### 16 — Logout

- [ ] Logout clears tokens, user, profile, discovery, booking receipt, appointment/reschedule state
- [ ] Back navigation does not reveal protected content
- [ ] Protected routes return to sign-in

## Usability (~390×844 or comparable phone)

- [ ] Forms fit; keyboard does not permanently cover submit
- [ ] Lists/detail/dialogs usable; no ordinary horizontal overflow
- [ ] Validation messages visible; touch targets usable
- [ ] Loading/offline states clearly visible
- [ ] Status not color-only; buttons have clear names; busy buttons not re-entrant

## Sign-off

| Field | Value |
|-------|--------|
| Executed by | |
| Date (UTC) | |
| Device/API | |
| Result | Pass / Fail / Blocked |
| Blocker (if any) | |
| Notes | |

## Privacy

Do not commit screenshots, videos, tokens, passwords, emulator userdata, or protected payloads.
Artifacts under `tests/HealthCare.EndToEndTests/artifacts/` remain gitignored.
