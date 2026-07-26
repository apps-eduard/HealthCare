# HealthCare End-to-End Tests

Real-browser smoke tests for Organization Admin critical workflows.

## Purpose

| Project | Responsibility |
|---------|----------------|
| `HealthCare.Web.Tests` | bUnit / component / page tests with fake services (Windows-friendly) |
| `HealthCare.EndToEndTests` | Playwright Chromium against real Web + BFF + API + temporary PostgreSQL; **plus** Patient Mobile API journey pack (PM-8 Layer A) |
| `HealthCare.IntegrationTests` | API HTTP tests with Testcontainers (Ubuntu docsvr) |

### Patient Mobile E2E (PM-8)

| Layer | What it is | What it is not |
|-------|------------|----------------|
| **A — Automated** | `PatientMobileMvpE2eTests` + `PatientE2eApi` against real API host (`E2eHostFixture`) — same contract the MAUI client uses (**12/12** on docsvr) | Not Playwright Web; not mocked HTTP; not “build only” |
| **B — Android runtime** | Checklist on emulator/device: [`PatientAndroidRuntimeChecklist.md`](PatientAndroidRuntimeChecklist.md) — **Pass** on `HealthCare_Pixel_API34` (API 34, 2026-07-26) | Not claimed complete without a real device/emulator session |

Preferred native UI automation (Appium / MAUI UITest) is **not** packaged in this repository. When no AVD/device is available, mark Layer B **pending** with the exact blocker; do **not** label Layer A as full mobile E2E.

```bash
# Patient pack only (docsvr):
dotnet test ./tests/HealthCare.EndToEndTests/HealthCare.EndToEndTests.csproj \
  --filter "FullyQualifiedName~PatientMobileMvpE2eTests" \
  --logger "console;verbosity=normal"
```

## Architecture

```
Playwright Chromium
    → HealthCare.Web (separate process, ephemeral port)
        → BFF cookie session
            → HealthCare.Api (separate process, ephemeral port)
                → Temporary PostgreSQL (Testcontainers)
```

Separate processes are used so Blazor Server SignalR and BFF cookie auth behave like a real deployment. `WebApplicationFactory` TestServer is not used for browser traffic.

## Environment variables

| Variable | Purpose |
|----------|---------|
| `HEALTHCARE_END_TO_END_TEST_HOST=true` | Disables HTTPS redirection; marks test-safe host behavior (does **not** bypass authz) |
| `HEALTHCARE_SKIP_STATIC_LOGGER_FLUSH=true` | Avoids Serilog static logger dispose races |
| `HEALTHCARE_E2E_HEADED=true` | Sets Playwright `HEADED=1` for troubleshooting (default is headless) |
| `DOCKER_PROBE=1` | Preferred on docsvr with local Docker socket |

Never point E2E at `app-postgres`. The fixture always creates a disposable PostgreSQL container.

`E2eHostFixture` starts API/Web via `dotnet run` **without** `--no-build` so Web/API binaries match the checked-out source (medical-note UI and workflow actions). Prefer a full `dotnet build` before the first E2E run on a machine.

## Windows

Do **not** run Playwright/Testcontainers E2E on Windows. Run unit / architecture / web tests only.

## Ubuntu docsvr

```bash
cd /home/speed/projects/HealthCare
git fetch origin && git checkout main && git pull --ff-only origin main

unset DOCKER_HOST DOCKER_CONTEXT TESTCONTAINERS_HOST_OVERRIDE
export DOCKER_PROBE=1
export HEALTHCARE_END_TO_END_TEST_HOST=true

dotnet restore
dotnet build

# Install Chromium once per machine/agent (after build so Playwright assets exist).
# Prefer PowerShell when available:
#   pwsh ./tests/HealthCare.EndToEndTests/bin/Debug/net10.0/playwright.ps1 install chromium
# On Ubuntu without pwsh / without root `install-deps`, use the bundled Node CLI + optional user-space libs:
cd ./tests/HealthCare.EndToEndTests/bin/Debug/net10.0
./.playwright/node/linux-x64/node .playwright/package/cli.js install chromium
cd /home/speed/projects/HealthCare
# If `ldd` reports missing ATK/X11 libraries and sudo is unavailable:
bash ./tests/HealthCare.EndToEndTests/scripts/install-chromium-user-libs.sh
# Prefer root deps when available: `.../cli.js install-deps chromium`

dotnet test ./tests/HealthCare.IntegrationTests/HealthCare.IntegrationTests.csproj -m:1
dotnet test ./tests/HealthCare.EndToEndTests/HealthCare.EndToEndTests.csproj --logger "console;verbosity=normal"
```

## Failure artifacts

On failure, screenshots are written under:

`tests/HealthCare.EndToEndTests/artifacts/`

This folder is gitignored. Do not commit traces, videos, cookies, or tokens.

## Parallelization

E2E tests share one host fixture and use an xUnit collection with `DisableParallelization = true`.

## Scenarios

- Organization Admin smoke suite (`OrganizationAdminSmokeTests`)
- Clinic Admin CA-1: login → Clinic Dashboard → org settings denied (`ClinicAdminDashboardSmokeTests`)
- Clinic Admin CA-2: login → Clinic Profile → edit → save → reload persistence (`ClinicAdminSettingsSmokeTests`)
- Clinic Admin CA-3: login → Staff → create receptionist → password reset; no clinic picker (`ClinicAdminStaffSmokeTests`)
- Clinic Admin CA-4: login → Doctors → availability exception → reload persistence; no clinic picker (`ClinicAdminDoctorsSmokeTests`)
- Clinic Admin CA-5: login → Patients → clinic-scoped list → status update → reload persistence; no clinic picker / medical notes (`ClinicAdminPatientsSmokeTests`)
- Clinic Admin CA-6: login → Appointments → clinic-scoped queue → mark no-show → reload persistence; no clinic picker / medical notes (`ClinicAdminAppointmentsSmokeTests`)
- Clinic Admin CA-7: login → Operations Health/Reminders → clinic-scoped data → retry failed reminder → Pending after reload; no clinic picker / Hangfire / org settings (`ClinicAdminOperationsSmokeTests`)
- Clinic Admin CA-8: login → Clinic Reports → appointment/doctor/operations aggregates; no clinic picker / export / org settings (`ClinicAdminReportsSmokeTests`)
- Clinic Admin CA-9: login → update clinic profile → Clinic Audit Logs → `clinic_profile_update` / safe summary Details; no picker / export / metadata / org settings (`ClinicAdminAuditSmokeTests`)
- Clinic Admin CA-10 hardening (`ClinicAdminHardeningSmokeTests`)
- Doctor DR-1: login → Doctor Dashboard → org settings denied → logout (`DoctorDashboardSmokeTests`)
- Doctor DR-2: login → My Profile → update display name → reload persistence; identity fields read-only; admin nav absent (`DoctorProfileSmokeTests`)
- Doctor DR-3/DR-5: login → My Availability self-lock → queue/calendar self defaults; Patients directory (Model A); Staff absent (`DoctorAvailabilitySmokeTests`)
- Doctor DR-10 clinical: own CheckedIn appointment → draft note → save → sign → amend → complete with confirmation (`DoctorClinicalWorkflowSmokeTests`)
- Doctor DR-10 completion without note: complete succeeds; no auto-created medical note (`DoctorCompletionWithoutNoteSmokeTests`)
- Doctor DR-10 denials: admin nav absent + direct restricted routes; peer clinic-B appointment/note concealed (`DoctorAccessDenialSmokeTests`)
- Doctor DR-10 responsive: 390×844 detail dialog + note/complete actions; restricted nav stays absent (`DoctorResponsiveWorkflowSmokeTests`)
- Patient PM-8 Layer A: registration/confirm helper, login/linkage, profile, clinic/Doctor discovery, book/list/cancel/reschedule, cutoff `409`, cross-patient `404`, restricted surfaces, logout revoke (`PatientMobileMvpE2eTests`) — **12/12**
- Patient PM-8 Layer B: Android runtime checklist (`PatientAndroidRuntimeChecklist.md`) — **Pass** on Pixel API 34 emulator

### Doctor DR-10 environment notes

| Item | Value |
|------|--------|
| Host | Ubuntu **docsvr** only (not Windows) |
| Browser | Playwright Chromium |
| Mode | Headless by default (`HEALTHCARE_E2E_HEADED=true` for headed) |
| Base URL | Ephemeral `http://127.0.0.1:{port}` from `E2eHostFixture` |
| Data | Development seed users + API-created unique appointments (`DoctorE2eApi`) |
| Desktop viewport | 1440×900 (default `E2ePageTestBase`) |
| Narrow viewport | 390×844 (responsive smoke) |

Do not commit `tests/HealthCare.EndToEndTests/artifacts/` screenshots or Playwright traces.

### Patient PM-8 environment notes

| Item | Value |
|------|--------|
| Host | Ubuntu **docsvr** for Layer A; Windows 11 + local emulator for Layer B |
| Framework | xUnit HTTP (Layer A); UI Automator dumps / manual checklist (Layer B) |
| Android | `HealthCare_Pixel_API34` (Google APIs x86_64, API 34, 1080×2400) |
| Mode | Layer A non-interactive; Layer B interactive emulator |
| Data | Development seed Patient + unique registered Patients; available-slot booking |
| Confirmation | Dev confirmation-token helper (no live email; App Links deferred) |
| Artifacts | Failures/local temp only; gitignored; no tokens/PII committed |