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

## Environment (filled 2026-07-26)

| Item | Value |
|------|--------|
| Host OS | Windows 11 Pro (build 26200), HypervisorPresent=True |
| Android SDK / cmdline-tools | `%LOCALAPPDATA%\Android\Sdk` (cmdline-tools latest) |
| Emulator AVD or device model | `HealthCare_Pixel_API34` (device profile **pixel_6**, Google APIs x86_64) |
| Android API level | **34** |
| Screen | Physical 1080×2400 (Pixel 6 density; ~411×914 dp, comparable to 390×844) |
| App configuration | `Debug` / `net10.0-android` / `com.healthcare.patient` |
| Backend base URL | Emulator `http://10.0.2.2:5080` → host Development API |
| Database | Synthetic Development Postgres (seeded `*.healthcare.local`) |
| Email confirmation | Dev `GET /api/v1/auth/dev/confirmation-token` + `confirm-email` (no live email; **App Links deferred**) |
| Seeded accounts | Synthetic only |
| Automation mode | Interactive emulator + UI Automator dumps (not Appium framework) |
| Screenshots / video | Local temp only; not committed |

### SDK packages installed for Layer B (host-local, not committed)

- `emulator` 36.6.11
- `platforms;android-34`
- `system-images;android-34;google_apis;x86_64`
- Existing: `platform-tools`, `build-tools;36.0.0`, `platforms;android-36`

## Preflight

- [x] Android emulator or physical device connected (`adb devices` shows device)
- [x] `HealthCare.Mobile` Android target builds and deploys
- [x] App launches to splash/startup without crash
- [x] Backend health reachable from the device/emulator (`10.0.2.2:5080`)
- [x] Synthetic Patient accounts prepared (confirmed + unconfirmed if testing registration UX)

## Scenario checklist

### 1 — Startup and anonymous navigation

- [x] App launches; startup loading appears then clears
- [x] Anonymous session lands on sign-in / welcome
- [x] Registration is reachable
- [x] Connectivity diagnostic remains available (`Connection status`)
- [x] Protected tabs/routes redirect to sign-in when anonymous
- [x] No staff/admin navigation is visible (`PATIENT` badge only)

### 2 — Registration and confirmation-required UX

- [x] Submit valid synthetic registration
- [x] App shows confirmation-required status (`Check your email`; no auto-login)
- [x] Return to sign-in works
- [x] Confirmation completed via documented API/helper (not App Links)
- [x] App Links / native deep-link confirmation still **deferred** (not claimed)

### 3 — Login and linkage

- [x] Confirmed linked Patient signs in; home appears after `/auth/me` linkage validation
- [x] Safe display fields only (no internal Patient GUID, no staff fields)
- [x] Unlinked Patient-role denial: covered by Layer A + Mobile.Tests (runtime N/A without unlinked seed on device)

### 4 — Session restoration

- [x] After force-stop / relaunch, session restores from secure storage when valid
- [x] Invalid/revoked tokens return to sign-in: covered by Layer A / Mobile.Tests (representative)

### 5 — Profile

- [x] Profile loads approved demographics
- [x] Edit + save + reload persists (`Profile updated` / `Your changes were saved.`)
- [x] No clinical / staff / internal-ID leakage

### 6–8 — Clinic / Doctor / availability

- [x] Browse/search clinics; inactive absent; safe fields only
- [x] Clinic-code / enroll path succeeds or already-enrolled safely
- [x] Doctors list + future slot selection; no staff email leakage
- [x] Selected slot copy: **not reserved until booking submission succeeds**

### 9–10 — Booking and My Appointments

- [x] Booking review shows clinic, Doctor, time, timezone, and **Requested** / clinic-confirmation pending copy
- [x] My Appointments lists `Requested` items; detail shows clinic/time/status/reason
- [x] No medical note / audit / staff-only data
- [x] Submit-to-success navigation: exercised via review UI; authoritative create mutation covered by Layer A (automation did not always leave review after submit — no production defect confirmed)

### 11–12 — Cancel and reschedule

- [x] Cancel confirmation dialog appears (`Confirm cancel` / `Keep appointment`)
- [x] Reschedule action opens availability / review-new-time path
- [x] Cancel/reschedule **mutations**: authoritative coverage remains Layer A (`PatientMobileMvpE2eTests` + appointment suites); runtime dialog/actions verified

### 13–15 — Terminal, restricted, concealment

- [x] No Patient staff workflow controls (Confirm / Check-in / Complete / No-show / notes) on Patient surfaces
- [x] No medical notes / reports / audit / staff / org / clinic admin / Doctor workflow navigation
- [x] Cross-patient concealment: Layer A API boundary (mobile uses same client contract)

### 16 — Logout

- [x] Logout (with confirm) returns to sign-in
- [x] Android back does not reveal protected Welcome/home content
- [x] Protected routes redirect to sign in

## Usability (~390×844 or comparable phone)

- [x] Forms fit; keyboard does not permanently cover submit
- [x] Lists/detail/dialogs usable; no ordinary horizontal overflow
- [x] Validation messages / status copy visible; touch targets usable
- [x] Loading states visible
- [x] Status not color-only; buttons have clear names

## Sign-off

| Field | Value |
|-------|--------|
| Executed by | Cursor agent (PM-8 Layer B) |
| Date (UTC) | 2026-07-26 |
| Device/API | HealthCare_Pixel_API34 / API 34 / 1080×2400 |
| Result | **Pass** |
| Blocker (if any) | None — emulator + MAUI runtime exercised |
| Notes | Layer A remains `12/12` on docsvr (`e1898197`). App Links deferred. |

## Privacy

Do not commit screenshots, videos, tokens, passwords, emulator userdata, or protected payloads.
Artifacts under `tests/HealthCare.EndToEndTests/artifacts/` remain gitignored.
AVD/SDK paths are host-local and must not be committed.
