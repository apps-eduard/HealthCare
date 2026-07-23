# HealthCare

Multi-clinic healthcare platform MVP built as an ASP.NET Core 10 modular monolith with PostgreSQL.

Authoritative design documents:

- [Architecture](Docs/architecture.md)
- [Development plan](Docs/development-plan.md)
- [Security](Docs/security.md)
- [Phase progress](Docs/phase-progress.md)

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --version` should report `10.x`)
- Docker Desktop (for local PostgreSQL and Testcontainers)
- Optional later: .NET MAUI workload for `HealthCare.Mobile` (Phase 11)

## Quick start

### 1. Clone and restore

```powershell
cd HealthCare
dotnet restore HealthCare.sln
```

### 2. PostgreSQL

The API is configured to use the shared development server database `healthcare_db`:

```text
Host=100.110.26.112;Port=5432;Database=healthcare_db;Username=appuser;Password=***
```

See `.env.example` and `src/HealthCare.Api/appsettings*.json`.

Optional local Docker Postgres remains available via `docker compose up -d postgres` if you override the connection string.

Prefer user secrets or environment variables when rotating credentials:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=100.110.26.112;Port=5432;Database=healthcare_db;Username=appuser;Password=YOUR_PASSWORD" --project src/HealthCare.Api
```

### 3. Apply migrations

```powershell
dotnet ef database update --project src/HealthCare.Infrastructure --startup-project src/HealthCare.Api
```

Install the EF tool once if needed:

```powershell
dotnet tool install --global dotnet-ef
```

### 4. Run the API

```powershell
dotnet run --project src/HealthCare.Api --launch-profile http
```

- API: http://localhost:5080
- Swagger (Development): http://localhost:5080/swagger
- Health: http://localhost:5080/health
- Readiness: http://localhost:5080/health/ready
- Hangfire dashboard (Development, PLATFORM_ADMIN): http://localhost:5080/hangfire
- Current user / permissions: `GET /api/v1/auth/me`, `GET /api/v1/auth/me/permissions`

Authorization uses a code-defined permission catalog (`Docs/authorization-matrix.md`). Controllers declare `[AuthorizePermission]`; tenant scope remains enforced in services.

- Medical notes: `/api/v1/appointments/{appointmentId}/medical-notes`, `/api/v1/medical-notes/{id}` (+ `/draft`, `/sign`, `/amend`). Requires clinical role + `medical_notes.*`. No MudBlazor UI yet.

### 5. Run the staff web app

```powershell
dotnet run --project src/HealthCare.Web --launch-profile http
```

- Staff UI: http://localhost:5018
- Configure API base URL via `Api:BaseUrl` in `src/HealthCare.Web/appsettings*.json` (default `http://localhost:5080/`)
- Sign in with a staff account (for example `clinicadmin@healthcare.local`)
- PLATFORM_ADMIN (`admin@healthcare.local`): use the platform tenant banner to search/select an organization (`OrganizationPicker`), then select a clinic. Free-text Organization IDs are not accepted. Selection is circuit-scoped only and cleared on logout.
- Staff clinic filter/create uses the clinic directory API (`/api/v1/staff-management/clinics`) via `ClinicPicker` — not free-text Clinic IDs. For PLATFORM_ADMIN, ClinicPicker requires the selected organization + `platformAdminBypass=true`.
- Organization directory API: `GET /api/v1/platform/organizations` (PLATFORM_ADMIN + `organizations.read` only)
- Appointments: `/appointments` (queue) and `/appointments/calendar` (day/week). Uses `appointments.*` / `availability.read` / `patients.search` permissions from `/auth/me`. Create flow uses `PatientPicker` + clinic doctors + available slots. Times display in the clinic `TimeZoneId` (API UTC). Mutations send `ExpectedVersion`.
- Auth: anonymous requests to protected pages challenge to `/login?returnUrl=...` (no 500). Staff Web uses an HttpOnly session cookie for host authentication (minimal claims; no API tokens). API access/refresh tokens remain in `ProtectedSessionStorage` (MVP). Prefer a full BFF cookie session before production hardening.
- Return URLs are validated as local paths only (`SafeReturnUrl`); external/`//` URLs fall back to `/dashboard`.
- Patients: `/patients` directory (requires `patients.search`). Detail and ClinicPatient status update require `patients.read` / `patients.update_clinic_status`. Uses typed `IStaffPatientApiClient`.
- Availability: `/availability` (requires `availability.manage_self`, `availability.manage_clinic`, or `availability.manage_organization`). Doctors manage self only (fixed clinic/doctor). Clinic admins pick a clinic doctor. Org admins use `ClinicPicker` then doctors. Weekly windows + date exceptions; times shown in clinic `TimeZoneId` (not browser-local). Mutations send `ExpectedVersion`. Optional slot preview via available-slots API. Typed `IDoctorAvailabilityApiClient`. `availability.read` alone does not open the management page.
- Medical notes: no staff UI yet; PLATFORM_ADMIN remains denied even with a selected organization.

### 6. Build and test

```powershell
dotnet build HealthCare.sln
dotnet test HealthCare.sln
```

Integration tests start PostgreSQL via Testcontainers and require Docker.

## Solution structure

```text
HealthCare/
├── src/
│   ├── HealthCare.Api/              # ASP.NET Core Web API host
│   ├── HealthCare.Web/              # Staff Blazor + MudBlazor web app
│   ├── HealthCare.Mobile/           # Patient MAUI placeholder (Phase 11)
│   ├── HealthCare.Domain/           # Domain entities by module
│   ├── HealthCare.Application/      # Use cases, validators, DI
│   ├── HealthCare.Infrastructure/   # EF Core, PostgreSQL, Identity storage
│   └── HealthCare.Contracts/        # Shared DTOs / contracts
├── tests/
│   ├── HealthCare.UnitTests/
│   ├── HealthCare.IntegrationTests/
│   └── HealthCare.ArchitectureTests/
├── Docs/
├── docker-compose.yml
├── Directory.Build.props
└── HealthCare.sln
```

## Current phase

Phase 1 — Solution foundation is in progress / complete for:

- Layered modular monolith projects
- PostgreSQL + EF Core DbContext foundation
- Serilog, Problem Details, correlation IDs
- OpenAPI/Swagger in Development
- `/health` endpoint
- Architecture, unit, and integration foundation tests

Next: Phase 2 — Identity and authentication.

## Configuration

| Setting | Source |
|---------|--------|
| `ConnectionStrings:DefaultConnection` | `appsettings*.json`, env, or user secrets |
| Serilog | `appsettings*.json` |
| Hangfire worker hosting | `Hangfire:*` in `appsettings*.json` / env |

### Hangfire

| Key | Development default | Production / base default |
|-----|---------------------|---------------------------|
| `Hangfire:Enabled` | `true` (local workers) | `false` |
| `Hangfire:WorkerCount` | `2` | `2`–`4` (only used when enabled) |
| `Hangfire:Queues` | `default`, `reminders`, `summaries` | same |
| `Hangfire:ScheduleRecurringJobs` | `true` | `false` |
| `Hangfire:Dashboard:Enabled` | `true` | `false` (independent of workers) |

Environment overrides:

```text
Hangfire__Enabled=true
Hangfire__WorkerCount=4
Hangfire__Queues__0=default
Hangfire__Queues__1=reminders
Hangfire__Queues__2=summaries
Hangfire__ScheduleRecurringJobs=true
Hangfire__Dashboard__Enabled=false
```

The API always registers Hangfire PostgreSQL storage/client so jobs can be enqueued even when local workers are disabled (external worker compatible). Enabling workers does **not** enable the dashboard. Recurring jobs are registered only when **both** `Hangfire:Enabled` and `Hangfire:ScheduleRecurringJobs` are true (run that on the process that hosts workers). Outside Development, workers and dashboard stay disabled unless explicitly enabled.

User secrets example:

```powershell
dotnet user-secrets init --project src/HealthCare.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=healthcare;Username=healthcare;Password=YOUR_LOCAL_PASSWORD" --project src/HealthCare.Api
```
