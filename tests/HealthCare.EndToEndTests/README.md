# HealthCare End-to-End Tests

Real-browser smoke tests for Organization Admin critical workflows.

## Purpose

| Project | Responsibility |
|---------|----------------|
| `HealthCare.Web.Tests` | bUnit / component / page tests with fake services (Windows-friendly) |
| `HealthCare.EndToEndTests` | Playwright Chromium against real Web + BFF + API + temporary PostgreSQL |
| `HealthCare.IntegrationTests` | API HTTP tests with Testcontainers (Ubuntu docsvr) |

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

# Install Chromium once per machine/agent (after build so playwright.ps1 exists):
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
# If the script is under the E2E project output:
pwsh ./tests/HealthCare.EndToEndTests/bin/Debug/net10.0/playwright.ps1 install chromium

dotnet test ./tests/HealthCare.IntegrationTests/HealthCare.IntegrationTests.csproj -m:1
dotnet test ./tests/HealthCare.EndToEndTests/HealthCare.EndToEndTests.csproj --logger "console;verbosity=normal"
```

## Failure artifacts

On failure, screenshots are written under:

`tests/HealthCare.EndToEndTests/artifacts/`

This folder is gitignored. Do not commit traces, videos, cookies, or tokens.

## Parallelization

E2E tests share one host fixture and use an xUnit collection with `DisableParallelization = true`.
