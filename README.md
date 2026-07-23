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

### 5. Build and test

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
│   ├── HealthCare.Web/              # Staff Blazor Web App (MudBlazor later)
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
