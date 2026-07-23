# Fluent UI design system — HealthCare Staff Web

## Package

| Package | Version | License |
|---------|---------|---------|
| `Microsoft.FluentUI.AspNetCore.Components` | **4.14.3** | MIT (open source) |
| `Microsoft.FluentUI.AspNetCore.Components.Icons` | **4.14.3** | MIT (open source) |

MudBlazor was fully removed from `HealthCare.Web`.

Registration: `builder.Services.AddFluentUIComponents()` in `Program.cs`.  
Toasts: `IUserNotificationService` → `FluentUserNotificationService` (wraps Fluent `IToastService`).

## Theme tokens

Defined in `wwwroot/css/healthcare-enterprise.css` (`--hc-*` variables):

| Token | Role | Value |
|-------|------|-------|
| `--hc-primary` / navy | Header, strong text | `#0F172A` |
| `--hc-accent` / teal | Primary actions, focus | `#0F766E` |
| `--hc-canvas` | Page background | `#F1F5F9` |
| `--hc-surface` | Cards / panels | `#FFFFFF` |
| `--hc-border` | Borders | `#E2E8F0` |
| `--hc-ink` | Body text | `#0F172A` |
| `--hc-muted` | Secondary text | `#64748B` |
| Success / Warning / Error / Info | Status | restrained green / amber / red / blue |

Shape: **4px** radius, minimal shadow, clear borders, compact controls.

Typography: IBM Plex Sans + Fluent defaults. Compact page titles; dense table text.

Spacing: **4px / 8px** scale via CSS utilities and Fluent stack spacing.

## Layout rules

- Stable header (`FluentHeader`) + left nav rail (`FluentNavMenu` / `FluentNavLink`)
- Permission-aware nav items
- PLATFORM_ADMIN tenant banner above content
- Main content uses soft canvas background
- Collapsible nav on smaller screens

## Grid / form / dialog rules

- Dense HTML tables (`.hc-table`) for server-paged lists (FluentDataGrid optional; current lists use compact tables to preserve server-side paging patterns)
- Forms: Fluent text/select/number fields or native inputs styled with enterprise CSS when Fluent pickers are insufficient (e.g. native `date` / `time`)
- Dialogs: Fluent `IDialogService` + dialog content components; primary/secondary actions; loading disables submit
- Status: shared `StatusBadge` + `StatusTone` (never color alone)

## Shared components

| Component | Purpose |
|-----------|---------|
| `PageHeader` | Title, description, actions |
| `StatusBadge` | Status chips |
| `PageLoading` / `EmptyState` / `ErrorState` / `PermissionDeniedState` | Consistent states |
| `IUserNotificationService` | Success/info/warning/error toasts |

## Responsive

- Desktop: full nav + multi-column filters  
- Tablet: collapsible nav, wrapped filters, horizontal table scroll  
- Small: stacked fields, drawer nav  

Primary target: desktop/tablet staff use.

## Accessibility

- Keyboard focus visible  
- Labeled form fields  
- Dialog close via secondary action / Fluent defaults  
- Status text + tone class  
- Icon buttons include `aria-label` where icon-only  

## Guidance for new pages

1. Use `PageHeader` + enterprise CSS classes.  
2. Prefer Fluent controls; native inputs only when Fluent lacks a picker.  
3. Map statuses through presentation helpers → `StatusTone` → `StatusBadge`.  
4. Notify via `IUserNotificationService` (never raw exceptions).  
5. Keep server-side filtering/paging; do not invent client-only authorization.

## Calendar note

Appointment calendar uses a **custom CSS Grid** day/week layout (no paid Fluent scheduler). Drag-drop and SignalR are out of scope.
