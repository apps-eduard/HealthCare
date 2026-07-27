# HealthCare k3s E2E environment (`doc-app`)

Lab overlay for automated end-to-end testing against the dedicated PostgreSQL database **`health_care_e2e`**.

## Verified infrastructure

| Item | Value |
|------|--------|
| Namespace | `doc-app` (existing) |
| Ubuntu / k3s Tailscale | `100.98.174.13` (`home-lab`) |
| PostgreSQL NodePort | `30432` |
| Verified Postgres endpoint | **`100.98.174.13:30432`** (TCP open) |
| Rejected typo | `100.98.17.13:30432` (TCP timeout) |
| E2E database | `health_care_e2e` only |
| Forbidden databases | `health_care_dev`, `health_care_staging`, production |

Do **not** point E2E workloads at `health_care_dev` or `health_care_staging`.

## Architecture

```
Playwright / Patient API E2E Job
    → Service healthcare-web-e2e:8080  (Blazor Interactive Server + BFF)
        → Service healthcare-api-e2e:8080
            → PostgreSQL 100.98.174.13:30432 / health_care_e2e
```

API container port **8080** (not host `5080`). Web container port **8080** (not host `5018`). Probes:

- API liveness `/health`, readiness `/health/ready`
- Web readiness/liveness `/login`

## Images / registry

No private registry is assumed. Lab approach:

1. Build on a machine with Docker (`scripts/e2e/build-images.sh`)
2. Import into k3s: `docker save … | sudo k3s ctr images import -`
3. Tags: `healthcare-*:e2e-<git-sha>` plus `*:e2e-local` for manifests

Alternative later: GHCR or a Rancher-managed registry.

## Secrets

Never commit real passwords. Create:

```bash
export HEALTHCARE_E2E_CONNECTION_STRING='Host=100.98.174.13;Port=30432;Database=health_care_e2e;Username=…;Password=…'
export HEALTHCARE_E2E_JWT_SIGNING_KEY='…32+ random chars…'
./scripts/e2e/create-secrets.sh
```

See `secret.example.yaml` for keys only.

### Dedicated DB role (recommended)

Run as a PostgreSQL administrator (password supplied interactively — not stored in Git). SQL template: `deploy/k8s/e2e/sql/create-e2e-role.sql.example`.

## ConfigMap

`healthcare-e2e-config` sets `ASPNETCORE_ENVIRONMENT=E2E`, Hangfire off, internal `Api__BaseUrl`, BFF HTTP cookie settings, and E2E base URLs.

Development seeders run under environment **`E2E`** (same as Development for seed only; Swagger remains Development-only).

## Deployment order

```bash
# On Ubuntu home-lab with kubectl + docker/k3s
chmod +x scripts/e2e/*.sh
./scripts/e2e/build-images.sh
# import images into k3s (see script output)

./scripts/e2e/create-secrets.sh
kubectl apply -f deploy/k8s/e2e/configmap.yaml
./scripts/e2e/migrate-e2e.sh          # must succeed before API traffic
./scripts/e2e/deploy-e2e.sh           # API + Web + optional Ingress
./scripts/e2e/prepare-e2e-db.sh       # migrate + idempotent seed
./scripts/e2e/run-e2e.sh              # HealthCare.EndToEndTests (external URL mode)
./scripts/e2e/status-e2e.sh
```

Remove only E2E resources:

```bash
./scripts/e2e/remove-e2e.sh
```

## External E2E test mode

When `HEALTHCARE_E2E_API_BASE_URL` and `HEALTHCARE_E2E_WEB_BASE_URL` are set, `E2eHostFixture` does **not** start Testcontainers or local `dotnet run` processes. It waits for `/health` and `/login` on the deployed services.

## Ingress

Optional hosts in `ingress.yaml` (Tailscale / hosts-file only). Do not expose publicly.

## Reset limitations

There is **no** automatic `DROP DATABASE` or full truncate Job. Prepare Job runs `Migrate` + idempotent seed. For a hard reset, an operator must truncate/rebuild `health_care_e2e` manually with DBA credentials, then re-run migrate/prepare. Never operate on `health_care_dev` / `health_care_staging`.

## Security warnings

- E2E uses HTTP inside the cluster and Development-style seed passwords by default — Tailscale-only.
- Do not log connection strings or JWT keys.
- Do not reuse the PostgreSQL `admin` role long-term for the API; prefer `healthcare_e2e_user`.

## Future staging

Add `deploy/k8s/overlays/staging` later; keep E2E isolated on `health_care_e2e`.
