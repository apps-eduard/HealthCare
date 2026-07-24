# Ant Design enterprise system — HealthCare Staff Web

## Package

| Package | Version | License |
|---------|---------|---------|
| `AntDesign` | **1.6.2** | MIT (open source) |

Visual reference: [Ant Design Pro Blazor](https://pro.antblazor.com/) (layout/spacing inspiration only — not demo branding).

Microsoft Fluent UI Blazor was fully removed from `HealthCare.Web`.

Registration:

```csharp
builder.Services.AddAntDesign();
builder.Services.AddScoped<IUserNotificationService, AntUserNotificationService>();
builder.Services.AddScoped<IUiModalService, AntUiModalService>();
```

Host (`App.razor`):

- `_content/AntDesign/css/ant-design-blazor.css`
- `_content/AntDesign/js/ant-design-blazor.js`
- `<AntContainer @rendermode="InteractiveServer" />` (required for Modal/Message/Confirm)

Application theme: `wwwroot/css/healthcare-ant-enterprise.css`.

## Theme tokens

| Token | Role | Value |
|-------|------|-------|
| `--hc-sider` | Dark sidebar | `#001529` |
| `--hc-primary` | Primary actions | `#1677ff` (Ant blue) |
| `--hc-accent` | Healthcare accent | `#0f766e` |
| `--hc-canvas` | Page background | `#f0f2f5` |
| `--hc-surface` | Cards / panels | `#ffffff` |
| `--hc-border` | Borders | `#f0f0f0` |
| Success / Warning / Error | Status | Ant palette |

Shape: **4px** radius, minimal shadow, compact controls, **8px** spacing scale.

## Layout

- `Layout` + dark `Sider` + sticky `Header` + `Content` (Pro-style)
- Permission-aware `Menu` / `MenuItem` with `RouterLink`
- Account `Dropdown` → Sign out → `/logout` (BFF antiforgery POST)
- `PlatformTenantBanner` for PLATFORM_ADMIN
- Responsive: sider collapses under ~992px

## Patterns

| Concern | Approach |
|---------|----------|
| Tables | Dense `.hc-table` HTML tables (server-side page data) |
| Filters | `.hc-filter-grid` + Ant Input/Select/DatePicker |
| Dialogs | `IUiModalService` → `ModalService.CreateModal` + `FeedbackComponent<T>` |
| Confirms | `IUiModalService.ConfirmAsync` → `ConfirmService` |
| Toasts | `IUserNotificationService` → `IMessageService` |
| Status | `StatusBadge` → Ant `Tag` via `StatusTone` |
| Calendar | Custom CSS Grid day/week (no paid scheduler) |
| Required labels | `FieldLabel.Mark` (visual only; app validates) |

## NU1900

`Directory.Build.props` keeps `NuGetAudit=true` and adds `NU1900` to `WarningsNotAsErrors` so transient nuget.org vulnerability-feed timeouts do not fail restore under `TreatWarningsAsErrors`. Advisory codes NU1901–NU1904 remain non-fatal as before; audit is not disabled.

## Known limitations

- No Ant Design Pro paid/commercial package — open-source `AntDesign` only
- Calendar is custom CSS Grid (day/week)
- Dense lists use enterprise HTML tables rather than Ant Table virtualization
- Some date/time UX uses Ant DatePicker / InputNumber; native inputs only where needed

## Guidance for new pages

1. Use `PageHeader` + `hc-card` / `hc-table` classes.
2. Prefer Ant Design controls; open dialogs via `IUiModalService`.
3. Map statuses through presentation helpers → `StatusTone` → `StatusBadge`.
4. Notify via `IUserNotificationService` (never raw exceptions).
5. Keep server-side filtering/paging; do not invent client-only authorization.
