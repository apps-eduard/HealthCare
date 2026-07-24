# MVP Organization Admin Scope

## 1. Purpose

The **Organization Admin** manages all clinics, staff, patients, appointments, and operational settings inside one organization.

The Organization Admin is responsible for:

- Organization-level clinic operations
- Clinic administration
- Staff administration
- Patient directory access
- Appointment operations
- Doctor availability
- Organization reporting
- Security and audit visibility within the organization

The Organization Admin must not access another organization and must not receive Platform Admin privileges.

---

## 2. Core Security Model

Every Organization Admin operation must require:

```text
ORGANIZATION_ADMIN role
+ Required permission
+ Active organization membership
+ Organization scope
+ Clinic scope when required
```

The Organization Admin:

- Can manage clinics inside the assigned organization
- Can manage authorized staff inside the assigned organization
- Can switch between clinics in the assigned organization
- Cannot select or access another organization
- Cannot assign `PLATFORM_ADMIN`
- Cannot bypass tenant isolation
- Cannot access medical notes by admin role alone

---

## 3. Organization Dashboard

The dashboard should display organization-scoped information:

- Total clinics
- Active clinics
- Total active staff
- Total doctors
- Total patients
- Today’s appointments
- Requested appointments
- Confirmed appointments
- Checked-in appointments
- Completed appointments
- Cancelled appointments
- No-show appointments
- Failed reminders
- Failed clinic summaries
- Recent organization audit events
- Clinics requiring attention

Do not display:

- Other organizations’ data
- Medical-note content
- Passwords
- Tokens
- Connection strings
- Sensitive system internals

---

## 4. Clinic Directory

### Organization Admin can

- List clinics in the organization
- Search clinics
- View clinic details
- Filter active and inactive clinics
- Select a clinic as the current working context
- View clinic timezone
- View clinic contact information
- View clinic address
- View clinic staff count
- View clinic doctor count
- View clinic appointment summary

### MVP management scope

The Organization Admin may:

- Create a clinic
- Update clinic profile
- Activate a clinic
- Deactivate a clinic
- Set clinic timezone
- Update clinic contact details
- Update clinic address

### MVP restrictions

- Clinic must remain inside the organization
- Clinic cannot move to another organization
- Clinic cannot be permanently deleted
- Deactivation must preserve historical records
- Clinic changes must be audited

---

## 5. Clinic Context Selector

The Organization Admin may select:

```text
Organization
  -> Fixed to assigned organization

Clinic
  -> One clinic
  -> All clinics, where supported
```

### UI requirements

- Organization displayed as read-only
- Searchable Clinic picker
- “All clinics” option for organization-wide lists
- No free-text Clinic ID
- Changing clinic clears stale page data
- Current clinic context visible in the header
- Clinic context clears when the session ends

### Security requirements

- Clinic selection is not authorization
- Backend validates clinic ownership
- Clinic must belong to the current organization
- Directly supplied out-of-organization Clinic IDs are rejected

---

## 6. Staff Management

### Organization Admin can

- List staff across all clinics in the organization
- Search staff
- Filter by clinic
- Filter by role
- Filter by active status
- View staff details
- Create staff accounts
- Update allowed staff profile fields
- Activate staff
- Deactivate staff
- Assign permitted roles
- Remove permitted roles
- Revoke user sessions
- View assignable roles
- Move staff between clinics only if explicitly supported later

### Assignable roles

The Organization Admin may assign:

- `ORGANIZATION_ADMIN`
- `CLINIC_ADMIN`
- `DOCTOR`
- `NURSE`
- `RECEPTIONIST`

The Organization Admin must not assign:

- `PLATFORM_ADMIN`
- `PATIENT` as a staff role
- Unknown or custom roles

### Safety rules

- No self-elevation
- No self-deactivation
- Cannot remove the last active Organization Admin
- Role changes revoke active sessions
- Inactive clinics cannot receive new staff
- Staff cannot be assigned outside the organization
- All role changes must be audited

---

## 7. Clinic Admin Management

The Organization Admin can:

- List Clinic Admins
- Create a Clinic Admin
- Assign a Clinic Admin to a clinic
- Activate or deactivate a Clinic Admin
- Revoke Clinic Admin sessions
- Replace a Clinic Admin
- View Clinic Admin status

### Restrictions

- Clinic Admin must belong to a clinic in the organization
- Clinic Admin cannot be assigned to another organization
- The last required Clinic Admin may be protected
- Clinic Admin role changes must be audited

---

## 8. Doctor and Clinical Staff Management

The Organization Admin can manage operational staff records.

### Features

- List doctors
- List nurses
- Filter by clinic
- View active or inactive status
- Create doctor accounts
- Create nurse accounts
- Activate or deactivate accounts
- Assign staff to clinics
- View specialty and job title
- Revoke sessions

### Restrictions

The Organization Admin must not:

- Grant medical-note permissions automatically
- Read medical notes by admin role alone
- Create clinical notes
- Sign clinical notes
- Operate as a doctor or nurse

Clinical permissions remain role-specific.

---

## 9. Patient Directory

### Organization Admin can

- Search patients across clinics in the organization
- Filter by clinic
- Search by patient name
- Search by local patient number
- View patient operational details
- View clinic enrollment status
- Update clinic-patient status
- View active or inactive patient status
- Select a patient for appointment creation

### Restrictions

The Organization Admin must not:

- Access patients outside the organization
- View medical notes by admin role alone
- View passwords or security fields
- View authentication tokens
- Permanently delete patients
- Merge patient identities in the MVP
- Transfer patients across organizations

---

## 10. Appointment Management

### Organization Admin can

- View appointments across clinics in the organization
- Filter by clinic
- Filter by date
- Filter by status
- Filter by doctor
- View appointment details
- Create an appointment
- Confirm an appointment
- Cancel an appointment
- Reschedule an appointment
- Check in a patient
- Mark no-show where authorized
- View appointment queue
- View day and week calendar

### Restrictions

The Organization Admin should not automatically:

- Complete appointments as a clinical action
- Write medical notes
- Change appointment status arbitrarily
- Access another organization’s appointments
- Bypass availability rules
- Bypass concurrency checks

The API remains authoritative for status transitions.

---

## 11. Doctor Availability

### Organization Admin can

- Select any clinic in the organization
- Select doctors in the selected clinic
- View weekly availability
- Create availability windows
- Edit availability windows
- Delete availability windows
- Add full-day exceptions
- Add unavailable time ranges
- Add custom available ranges if supported
- Delete exceptions
- Preview available slots
- View clinic-local timezone

### Rules

- Doctor must belong to a clinic in the organization
- Availability changes use optimistic concurrency
- Overlapping windows are rejected
- Clinic timezone controls local display
- Cross-organization doctor access is denied

---

## 12. Appointment Reminders and Clinic Summaries

The Organization Admin can:

- View reminder status
- View failed reminders
- Retry failed reminders
- View clinic-summary status
- Retry failed clinic summaries
- Filter by clinic
- View safe operational delivery details

Do not display:

- Message-provider secrets
- Patient medical information
- Full notification payloads
- Sensitive job arguments

---

## 13. Organization Settings

### Organization Admin can manage

- Organization name
- Contact email
- Contact phone
- Country
- Default settings where supported
- Organization branding placeholder
- Operational timezone defaults
- Organization status summary
- Clinic limits as read-only if controlled by Platform Admin

### Restrictions

The Organization Admin cannot:

- Suspend the organization
- Change subscription status
- Change billing plan
- Increase platform-enforced limits
- Delete the organization
- Change the organization’s platform ownership

These remain Platform Admin responsibilities.

---

## 14. Organization Usage

The Organization Admin can view:

- Number of clinics
- Number of staff
- Number of active doctors
- Number of patients
- Monthly appointment count
- Current platform limits
- Remaining clinic capacity
- Remaining staff capacity
- Limit warnings

The Organization Admin cannot increase limits.

Limit changes are controlled by the Platform Admin.

---

## 15. Organization Reports

### MVP reports

- Daily appointment summary
- Appointment count by clinic
- Appointment count by status
- Appointment count by doctor
- Cancellation count
- No-show count
- Staff count by clinic
- Patient count by clinic
- Availability coverage summary
- Failed reminder summary

### Export scope

For MVP:

- On-screen reports
- Optional CSV export for safe operational data

Do not export:

- Medical-note content
- Password or token data
- Full patient clinical records
- Other organizations’ data

---

## 16. Organization Audit Logs

The Organization Admin can view organization-scoped audit events.

### Events

- Clinic created
- Clinic updated
- Clinic activated
- Clinic deactivated
- Staff created
- Staff updated
- Staff activated
- Staff deactivated
- Role assigned
- Role removed
- Session revoked
- Appointment created
- Appointment confirmed
- Appointment cancelled
- Appointment rescheduled
- Patient clinic status changed
- Availability changed
- Reminder retried
- Clinic summary retried
- Cross-clinic access denied

### Filters

- Date range
- Clinic
- Acting user
- Action
- Result
- Correlation ID

### Privacy rules

Audit logs must not contain:

- Passwords
- Tokens
- Refresh tokens
- Medical-note content
- Sensitive patient payloads
- Full request bodies

---

## 17. Security Operations

The Organization Admin can:

- Revoke sessions for users in the organization
- Deactivate compromised staff accounts
- View failed-login summaries for organization users
- View authorization-denial summaries
- Review suspicious cross-clinic attempts
- Review organization audit events

### Restrictions

The Organization Admin cannot:

- Revoke Platform Admin sessions
- Manage another organization
- View raw tokens
- View passwords
- Access server secrets
- Open the Hangfire dashboard unless separately permitted
- Change platform security settings

---

## 18. Medical Notes Boundary

The Organization Admin role alone has no medical-note access.

The Organization Admin must not automatically:

- Read medical notes
- Create medical notes
- Update medical-note drafts
- Sign medical notes
- Amend medical notes
- View diagnoses or prescriptions
- View clinical attachments

A user who also holds an authorized clinical role must still pass:

```text
Clinical permission
+ Active clinical membership
+ Clinic scope
+ Appointment relationship
```

Administrative authority does not imply clinical access.

---

## 19. Organization Admin Permissions

```text
organizations.read
organizations.update_own

clinics.read
clinics.create
clinics.update
clinics.activate
clinics.deactivate

staff.read
staff.manage
staff.password_reset

roles.read
roles.assign

security_sessions.revoke

patients.read
patients.search
patients.update_clinic_status

appointments.read
appointments.create
appointments.confirm
appointments.check_in
appointments.cancel
appointments.reschedule
appointments.no_show

availability.read
availability.manage_organization

reminders.read
reminders.retry

summaries.read
summaries.retry

organization_reports.read

organization_audit_logs.read

security_sessions.read
security_sessions.revoke
```

The Organization Admin must not receive:

```text
organizations.select_global
organizations.suspend
platform_users.manage
platform_health.read
platform_jobs.manage
subscriptions.manage
organization_limits.manage
medical_notes.*
```

---

## 20. Organization Admin Menu

```text
Dashboard

Clinics
  Clinic Directory
  Create Clinic
  Clinic Details

Appointments
  Appointment Queue
  Calendar

Patients
  Patient Directory

Scheduling
  Doctor Availability

Staff
  Staff Management
  Clinic Admins
  Doctors
  Nurses
  Receptionists

Operations
  Reminders
  Clinic Summaries
  Reports

Security
  Session Revocation
  Audit Logs

Organization
  Organization Profile
  Usage and Limits

Account
  My Profile
  Logout
```

---

## 21. Organization Admin Must Not Do

The Organization Admin must not:

- Access another organization
- Assign `PLATFORM_ADMIN`
- Manage Platform Admin users
- Suspend the organization
- Change subscription status
- Increase organization limits
- Permanently delete clinics
- Permanently delete staff
- Permanently delete patients
- View passwords or tokens
- Read medical notes by admin role alone
- Create or sign medical notes by admin role alone
- Complete appointments as an admin-only action
- Bypass clinic or organization scope
- Access the global platform dashboard
- Manage platform background workers
- View platform-wide audit logs

---

## 22. MVP Priority Order

### Phase 1 — Required

1. Organization dashboard
2. Clinic directory
3. Create and update clinic
4. Activate and deactivate clinic
5. Staff management
6. Clinic Admin management
7. Doctor and nurse management
8. Clinic selector
9. Patient directory
10. Appointment queue and calendar

**Backend status (2026-07-24):**
- Organization-scoped staff APIs under `/api/v1/staff-management` (clinic reassignment, password-reset foundation, session revocation, `GET .../clinic-admins`, last Organization Admin protection, role hierarchy). Production password email sender remains an abstraction (Development captures tokens safely).
- Organization-scoped patient directory and clinic enrollment on `/api/v1/staff/patients` (search, detail with enrollment list, clinic-profile status with optional ClinicId targeting, appointment-safe `GET .../lookup`) plus org-aware `POST /api/v1/clinics/{clinicId}/patients/{patientId}/enroll`. Patient self-scope remains denied for staff patient APIs. Audited via `IAuthorizationAuditLogger.PatientOperation`.
- Organization-scoped appointment queue/calendar on `/api/v1/staff/appointments`, `/queue`, `/calendar` with create/confirm/check-in/cancel/reschedule/no-show. Org Admin does **not** receive `appointments.complete`. Audited via `IAuthorizationAuditLogger.AppointmentOperation`. No migration required for this slice.
- Organization-scoped doctor availability on `/api/v1/staff/doctors/{id}/availability*` plus `GET /api/v1/staff/clinics/{clinicId}/doctors` (validated ClinicId). Optional `clinicId` query must match the doctor’s clinic. Responses include `ClinicTimeZoneId`. Audited via `IAuthorizationAuditLogger.AvailabilityOperation`. No migration required.
- Organization-scoped reminder and clinic-summary operations on `GET /api/v1/staff/reminders`, appointment reminder list/retry, `GET /api/v1/staff/appointment-summary-runs`, summary get/retry, and `GET /api/v1/staff/operations/health` (safe sender/Hangfire flags only). Responses include `BackgroundJobId` correlation; no provider secrets or delivery payloads. Audited via `ReminderOperation` / `SummaryOperation`. No migration required.
- Organization-scoped operational reports on `/api/v1/organization/reports/*` (appointments, staff, patients, availability, reminder-failures, summary-failures) with optional validated `ClinicId`, date-range hardening (max 93 days), and safe CSV export (`…/export.csv`). Permission `organization_reports.read`. Audited via `ReportOperation`. No migration required.
- Organization-scoped security operations on `/api/v1/organization/security/*` (session visibility without token secrets, session revoke, compromised-account deactivate+revoke, failed-login / authorization-denial / cross-clinic attempt summaries). Permission `security_sessions.read` (+ `security_sessions.revoke` for mutations). Persisted `SecurityEvents` (`AddSecurityEvents` migration). Audited via `SecurityOperation`.
- Organization-scoped audit-log query on `/api/v1/organization/audit-logs` (list, detail by id, correlation-id lookup) with filters/pagination and `AuditRetention` options foundation. Permission `organization_audit_logs.read`. Persisted `OrganizationAuditEvents` + org `MaxClinics`/`MaxStaff` (`AddOrganizationAuditAndUsageLimits` migration). Operational audits persist via `IOrganizationAuditRecorder` from `AuthorizationAuditLogger` (including `ClinicOperation`).
- Organization usage and limit visibility on `GET /api/v1/organization/usage` (`organization_usage.read`) with remaining capacity and warning flags. Clinic/staff create enforce platform limits (`clinic.limit_reached` / `staff.limit_reached`). Org Admin cannot increase limits.

### Phase 2 — Scheduling and operations

11. Doctor availability
12. Appointment actions
13. Reminder status and retry
14. Clinic-summary status and retry
15. Organization reports

### Phase 3 — Security and governance

16. Session revocation
17. Organization audit logs
18. Usage and limit visibility
19. Organization profile settings

---

## 23. Deferred After MVP

- Billing and payments
- Subscription changes
- Custom pricing plans
- Organization suspension
- Permanent deletion
- Custom role builder
- Medical-note UI
- Clinical attachments
- Insurance claims
- Payroll
- Inventory
- Pharmacy
- Laboratory integration
- Advanced analytics
- Cross-organization transfers
- Organization impersonation
- Real-time SignalR updates
- Drag-and-drop appointment scheduling
