# HealthCare.Mobile — Patient MAUI foundation (PM-2)

Android-first **.NET MAUI Blazor Hybrid** app for the Patient Mobile MVP.

**Status:** PM-2 foundation only. PM-3 (full auth/profile UX) through PM-8 are **not** started.

Authoritative product scope: [`Docs/mvp-patient-scope.md`](../../Docs/mvp-patient-scope.md).

## Projects

| Project | Role |
|---------|------|
| `HealthCare.Mobile` | MAUI Blazor Hybrid Android app (`net10.0-android`) |
| `HealthCare.Mobile.Core` | Testable DI/config/API/auth/storage abstractions (`net10.0`) |
| `HealthCare.Mobile.Tests` | Foundation unit tests |

Shared contracts only: `HealthCare.Contracts`. The mobile app does **not** reference Web, Application, or Infrastructure.

## Supported targets

- **Primary:** Android (`net10.0-android`, min API 24)
- **Not a PM-2 release requirement:** iOS / Mac Catalyst / Windows (template platform folders may exist; TFM is Android-only)

## Structure (app)

- `MauiProgram.cs` — DI, configuration, BlazorWebView
- `Components/` — Blazor pages, layout, shared UI states, route guard
- `Storage/MauiSecureTokenStore.cs` — MAUI `SecureStorage` (no plaintext fallback)
- `Platforms/Android/` — manifest + `network_security_config.xml`
- `appsettings*.json` — embedded environment config
- `wwwroot/` — static assets

## Configuration

Section: `Mobile` (`MobileAppOptions`).

| Setting | Purpose |
|---------|---------|
| `EnvironmentName` | Development / Emulator / Device / Staging / Production |
| `ApiBaseUrl` | Absolute API base (no trailing slash required) |
| `HttpTimeoutSeconds` | 5–120 |
| `AllowCleartextHttp` | Dev/emulator HTTP only; **must be false in Production** |

### Android networking

| Scenario | Typical `ApiBaseUrl` |
|----------|----------------------|
| Android emulator → API on host | `http://10.0.2.2:5080` (cleartext, Dev only) |
| Physical device → host on LAN | `http://<host-lan-ip>:5080` (Dev) or HTTPS |
| Shared / docsvr | HTTPS base URL for that environment |
| Production | HTTPS only; cleartext forbidden |

Notes:

- Emulator `10.0.2.2` is the host loopback alias.
- Dev HTTPS with the ASP.NET dev certificate often fails on Android unless the cert is trusted on the device/emulator — prefer documented Dev HTTP to the emulator alias, or a proper trusted cert.
- Cleartext is restricted in `network_security_config.xml` to `10.0.2.2` / localhost / `127.0.0.1`.
- Override at runtime: set env `HEALTHCARE_API_BASE_URL` (host builds) or `HEALTHCARE_MOBILE_ENV=Emulator`.
- **Do not** disable TLS validation globally. **Do not** commit secrets, tokens, or private machine credentials.

## Secure storage

- Refresh + access tokens and expiry timestamps via `ISecureTokenStore` → MAUI `SecureStorage`
- Cleared on logout and failed refresh
- Never logged or shown in UI
- No email/password persistence
- No silent Preferences fallback for tokens

## Authentication-state foundation

`IAuthSessionService` / `AuthSession`:

- Anonymous vs authenticated (token pair present)
- Access/refresh + expiry awareness
- Current-user + patient-linkage hooks (from `/auth/me`, not JWT claims alone)
- Startup restore from secure storage
- Clear session on logout / refresh failure
- Route guard: authenticated destinations redirect to `/sign-in` when anonymous

Backend remains authoritative for Patient identity and authorization.

## Typed API client

- Named `HttpClient`s: anonymous + authenticated
- Bearer injection + **one** refresh retry on `401` (no infinite loop)
- Problem mapping: `401` / `403` / `404` / `409` / validation / network / timeout / server
- User-facing messages never include stack traces, SQL, or raw protected payloads

## Navigation (placeholders)

| Route | Milestone |
|-------|-----------|
| `/` startup, `/connectivity` smoke | PM-2 |
| `/sign-in`, `/register` | PM-3 (sign-in page is **dev smoke only** in PM-2) |
| `/home`, `/profile` | PM-3+ |
| `/clinics` | PM-4 |
| `/appointments` | PM-6 |

## Restore / build / run

```powershell
dotnet workload install maui-android
dotnet restore HealthCare.sln
dotnet build src/HealthCare.Mobile/HealthCare.Mobile.csproj -f net10.0-android
dotnet test tests/HealthCare.Mobile.Tests/HealthCare.Mobile.Tests.csproj
```

Run on an emulator/device from Visual Studio / `dotnet build -t:Run -f net10.0-android` with a selected Android target. Start the API so the smoke page can reach `/health`.

## Connectivity smoke

`/connectivity` calls `GET /health` using the configured base URL. Dev sign-in on `/sign-in` exists only to prove token storage + authenticated client wiring — **not** completed PM-3 UX.

## Known limitations (PM-2)

- No full registration, email confirmation, profile, discovery, booking, or appointments UX
- No Google / OTP
- No Patient E2E pack
- Android emulator launch may be unavailable in CI/agent environments; Android **build** is still required
- iOS not a release target in this milestone
