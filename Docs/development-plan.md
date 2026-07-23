# HealthCare MVP Development Plan

## 1. Purpose

This plan defines the order in which Cursor should build the HealthCare MVP.

The project must be implemented in small, reviewable phases. Cursor must not attempt to generate the complete application in one prompt.

Each phase must be completed, built, tested, and reviewed before starting the next phase.

---

## 2. MVP goal

Deliver a usable multi-clinic platform where:

- A patient registers once using Google or email.
- A patient can discover multiple clinics.
- A patient can book appointments with different clinics.
- A patient can view their own appointments and allowed records.
- Each clinic can manage only its own patients, appointments, and medical notes.
- Clinic A cannot access Clinic B's private records.
- Clinic B cannot access Clinic A's private records.
- Staff use a MudBlazor web application.
- Patients use a .NET MAUI Blazor Hybrid application.

---

## 3. Delivery strategy

Use the following implementation strategy:

```text
Foundation
   ↓
Identity and authorization
   ↓
Organizations and clinics
   ↓
Patients and clinic registration
   ↓
Staff and doctors
   ↓
Appointments
   ↓
Medical notes
   ↓
Notifications and Hangfire
   ↓
Mobile patient experience
   ↓
Security hardening
   ↓
Deployment
```

---

## 4. Phase 0 - Repository and documentation

### Objectives

- Create the Git repository.
- Add the three design documents.
- Add root README.
- Add `.gitignore`.
- Add `.editorconfig`.
- Add `Directory.Build.props`.
- Define code style and nullable reference types.

### Deliverables

```text
HealthCare.sln
README.md
docs/architecture.md
docs/development-plan.md
docs/security.md
.editorconfig
.gitignore
Directory.Build.props
```

### Acceptance criteria

- Repository builds with an empty solution.
- Documentation is committed.
- Cursor is instructed to read all three documents before implementation.

---

## 5. Phase 1 - Solution foundation

### Objectives

Create the base projects:

```text
src/HealthCare.Api
src/HealthCare.Web
src/HealthCare.Mobile
src/HealthCare.Domain
src/HealthCare.Application
src/HealthCare.Infrastructure
src/HealthCare.Contracts

tests/HealthCare.UnitTests
tests/HealthCare.IntegrationTests
tests/HealthCare.ArchitectureTests
```

### Required packages

Use only packages required for this phase.

Expected packages include:

- Microsoft.EntityFrameworkCore
- Npgsql.EntityFrameworkCore.PostgreSQL
- Microsoft.AspNetCore.Identity.EntityFrameworkCore
- FluentValidation.AspNetCore
- Serilog.AspNetCore
- Swashbuckle.AspNetCore or built-in OpenAPI support
- xUnit
- FluentAssertions
- Testcontainers.PostgreSql

### Tasks

- Configure dependency injection.
- Configure app settings.
- Configure PostgreSQL connection.
- Add health checks.
- Add Problem Details.
- Add Serilog.
- Add OpenAPI.
- Add API version prefix conventions.
- Add correlation ID middleware.
- Add a simple `/health` endpoint.

### Acceptance criteria

- Solution builds.
- API starts locally.
- Swagger or OpenAPI is available in development.
- PostgreSQL connection works.
- Integration test can start PostgreSQL using Testcontainers.
- Health endpoint returns success.

---

## 6. Phase 2 - Identity and authentication

### Objectives

Implement global user identity for staff and patients.

### Tasks

- Add `ApplicationUser` based on ASP.NET Core Identity.
- Add Identity EF Core configuration.
- Add staff email/password login.
- Add patient Google authentication.
- Add optional patient email/password registration.
- Add JWT access tokens.
- Add refresh tokens.
- Hash refresh tokens in the database.
- Implement refresh token rotation.
- Implement logout and token revocation.
- Add account activation and deactivation.

### Required endpoints

```text
POST /api/v1/auth/register/patient
POST /api/v1/auth/google
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
GET  /api/v1/auth/me
```

### Tests

- Valid login succeeds.
- Invalid login fails without revealing whether the account exists.
- Disabled account cannot log in.
- Refresh token rotation works.
- Reused refresh token revokes its token family.
- Google token validation rejects invalid tokens.

### Acceptance criteria

- Authentication works through Swagger or API test client.
- No plaintext refresh token is stored.
- No sensitive token is written to logs.
- Identity migrations are included.

---

## 7. Phase 3 - Roles and authorization foundation

### Roles

```text
PLATFORM_ADMIN
ORGANIZATION_ADMIN
CLINIC_ADMIN
DOCTOR
NURSE
RECEPTIONIST
PATIENT
```

### Objectives

Implement role-based and policy-based authorization.

### Tasks

- Seed role definitions.
- Add current-user service.
- Add current-staff context.
- Add current-patient context.
- Add organization scope policy.
- Add clinic scope policy.
- Add patient self-scope policy.
- Add permission constants.
- Add authorization handlers.

### Tests

- Patient cannot call staff endpoints.
- Receptionist cannot access platform administration.
- Clinic staff cannot access another clinic.
- Organization admin cannot access another organization.
- Patient cannot access another patient's data.

### Acceptance criteria

- Authorization is tested using integration tests.
- Controllers or endpoints do not manually trust client-supplied clinic IDs.

---

## 8. Phase 4 - Organizations and clinics

### Objectives

Implement organization and clinic management.

### Tasks

- Create `Organization` entity.
- Create `Clinic` entity.
- Add EF Core configurations.
- Add migrations.
- Add organization administration endpoints.
- Add clinic administration endpoints.
- Add public clinic directory endpoints.
- Add specialty and city filters.
- Add clinic active/inactive status.

### Staff web screens

- Organization list for platform admin
- Organization details
- Clinic list
- Clinic create/edit form
- Clinic profile

### Patient endpoints

```text
GET /api/v1/clinics
GET /api/v1/clinics/{clinicId}
```

### Tests

- Organization admin sees only their organization.
- Clinic admin sees only their clinic.
- Public directory returns active clinics only.
- Duplicate clinic slug is rejected.

### Acceptance criteria

- Clinic directory can be displayed with seed data.
- Organization and clinic access boundaries are enforced.

---

## 9. Phase 5 - Patients and clinic-patient registration

### Objectives

Create one global patient profile and one clinic relationship per clinic.

### Tasks

- Create `Patient` entity.
- Create `ClinicPatient` entity.
- Add unique constraint on `(ClinicId, PatientId)`.
- Add patient profile endpoints.
- Add clinic registration workflow.
- Generate local patient numbers.
- Add clinic patient search for authorized staff.

### Required behavior

When a patient books at a clinic for the first time:

1. Confirm the patient account exists.
2. Find the clinic.
3. Find or create `ClinicPatient`.
4. Never duplicate the relationship.
5. Record audit events.

### Endpoints

```text
GET   /api/v1/patient/profile
PATCH /api/v1/patient/profile
GET   /api/v1/patient/clinics
GET   /api/v1/staff/patients
GET   /api/v1/staff/patients/{clinicPatientId}
```

### Tests

- A patient can register with Clinic A.
- The same patient can register with Clinic B.
- Clinic A sees only its own `ClinicPatient` record.
- Clinic B sees only its own `ClinicPatient` record.
- Duplicate clinic registration is prevented.
- Patient cannot access another patient's profile.

### Acceptance criteria

- Multi-clinic patient registration works.
- Clinic isolation tests pass.
- Patient self-scope tests pass.

---

## 10. Phase 6 - Staff and doctors

### Objectives

Allow each clinic to manage its own staff and doctors.

### Tasks

- Create `StaffMember` entity.
- Link staff to `ApplicationUser`.
- Link staff to organization and clinic.
- Add role assignment.
- Add doctor profile fields.
- Add doctor schedule and availability.
- Add staff activation/deactivation.

### Staff web screens

- Staff list
- Create staff
- Edit staff
- Doctor details
- Doctor availability

### Tests

- Clinic admin can manage staff in their clinic.
- Clinic admin cannot manage another clinic's staff.
- Disabled staff cannot access clinic endpoints.
- Receptionist permissions differ from doctor permissions.

### Acceptance criteria

- Clinic has at least one schedulable doctor.
- Patient app can retrieve doctor availability.

---

## 11. Phase 7 - Appointment booking

### Objectives

Implement the complete appointment workflow.

### Tasks

- Create `Appointment` entity.
- Add appointment status enum.
- Add appointment booking validation.
- Add conflict detection.
- Add appointment confirmation.
- Add appointment cancellation.
- Add patient appointment history.
- Add staff appointment queue.
- Add audit events.

### Endpoints

```text
GET    /api/v1/clinics/{clinicId}/doctors
GET    /api/v1/clinics/{clinicId}/availability
POST   /api/v1/appointments
GET    /api/v1/patient/appointments
GET    /api/v1/patient/appointments/{id}
PATCH  /api/v1/patient/appointments/{id}/cancel
GET    /api/v1/staff/appointments
PATCH  /api/v1/staff/appointments/{id}/status
```

### Rules

- Patient must book for themselves in the MVP.
- Patient cannot select another patient's ID.
- Staff can access only appointments from their clinic.
- Appointment clinic must match the clinic-patient relationship.
- Doctor must belong to the same clinic.
- Overlapping appointments must be prevented.
- Completed appointments cannot be cancelled.

### Tests

- Patient can book Clinic A.
- Same patient can separately book Clinic B.
- Clinic A staff cannot see Clinic B appointment.
- Clinic B staff cannot see Clinic A appointment.
- Patient cannot see another patient's appointment.
- Double booking is rejected.
- Invalid status transitions are rejected.

### Acceptance criteria

- End-to-end booking works through API.
- Staff can view and manage their clinic appointments.
- Patient can view only their own appointments.

---

## 12. Phase 8 - Staff web application

### Objectives

Build the MVP staff interface using MudBlazor.

### Screens

- Login
- Dashboard
- Today's appointments
- Appointment calendar
- Appointment details
- Patient list
- Patient details
- Medical notes
- Staff management
- Clinic settings
- Audit log viewer

### UI rules

- Use a consistent theme.
- Use responsive layout.
- Use loading states.
- Use empty states.
- Use validation messages.
- Use confirmation dialogs for destructive actions.
- Never hide authorization mistakes only in the UI; the API must enforce access.

### Acceptance criteria

- Clinic staff can complete daily appointment tasks.
- UI does not show data from other clinics.
- Unauthorized navigation returns an access denied experience.

---

## 13. Phase 9 - Medical notes

### Objectives

Allow authorized clinical staff to create clinic-private notes.

### Tasks

- Create `MedicalNote` entity.
- Support note types.
- Allow doctor and nurse note creation.
- Allow optional patient visibility.
- Prevent receptionist access to note contents unless explicitly permitted.
- Add audit logging for creation and viewing.

### Endpoints

```text
GET  /api/v1/staff/patients/{clinicPatientId}/medical-notes
POST /api/v1/staff/patients/{clinicPatientId}/medical-notes
GET  /api/v1/patient/records
```

### Tests

- Clinic A cannot access Clinic B notes.
- Patient sees only their own notes.
- Patient sees only notes marked visible.
- Receptionist cannot read clinical note content.
- Viewing a note creates an audit event where required.

### Acceptance criteria

- Clinical notes are isolated and protected.
- Patient-visible records are grouped by clinic.

---

## 14. Phase 10 - Hangfire and notifications

### Objectives

Add reliable background processing.

### Tasks

- Configure Hangfire with PostgreSQL-compatible storage.
- Protect the Hangfire dashboard.
- Add confirmation email job.
- Add SMS abstraction.
- Add appointment reminder job.
- Add daily clinic summary job.
- Add failed job retry policy.
- Add idempotency protection.

### Tests

- Booking enqueues a confirmation job.
- Cancelled appointment does not receive reminder.
- Retried job does not send duplicate notification.
- Staff from one clinic cannot access another clinic's notification data.

### Acceptance criteria

- API response is not delayed by email or SMS.
- Failed jobs are visible and retryable.
- Dashboard requires authorized administrator access.

---

## 15. Phase 11 - Patient mobile application

### Objectives

Build the Android-first patient experience.

### Screens

- Splash and authentication
- Google sign-in
- Clinic directory
- Clinic details
- Doctor list
- Availability
- Book appointment
- My appointments
- Appointment details
- Cancel appointment
- My records grouped by clinic
- Profile
- Notifications

### Security requirements

- Store access and refresh tokens securely.
- Clear local tokens on logout.
- Do not persist medical data unnecessarily.
- Do not trust client-provided patient IDs.

### Acceptance criteria

- Android app supports the full patient MVP flow.
- A patient can use one account across multiple clinics.
- Records remain visually separated by clinic.

---

## 16. Phase 12 - Audit and security hardening

### Objectives

Verify all access boundaries and critical actions.

### Tasks

- Complete audit logging.
- Add rate limiting.
- Add secure headers.
- Add account lockout.
- Add password policy.
- Review CORS.
- Review token lifetime.
- Review refresh token rotation.
- Review log redaction.
- Add abuse and authorization tests.
- Add dependency vulnerability scanning.

### Mandatory security tests

- Cross-organization access fails.
- Cross-clinic access fails.
- Cross-patient access fails.
- Manipulated URL ID does not bypass authorization.
- Manipulated request body clinic ID does not bypass authorization.
- Expired and revoked tokens fail.
- Disabled accounts fail.
- Patient role cannot access staff APIs.

### Acceptance criteria

- Security test suite passes.
- No high-severity known package vulnerabilities remain without written justification.

---

## 17. Phase 13 - Docker and deployment

### Objectives

Prepare the MVP for deployment to Ubuntu Server.

### Services

```text
nginx
healthcare-api
healthcare-web
postgresql
hangfire-worker or healthcare-api worker
```

### Tasks

- Add Dockerfiles.
- Add Docker Compose.
- Add environment variable configuration.
- Add production logging.
- Add database migration strategy.
- Add Nginx configuration.
- Add HTTPS setup documentation.
- Add backup and restore documentation.
- Add health checks.

### Acceptance criteria

- Fresh deployment starts successfully.
- Database migrations run safely.
- HTTPS works.
- Application restarts without losing queued jobs.
- PostgreSQL backup and restore are tested.

---

## 18. MVP release checklist

### Functional

- Patient can register and log in.
- Patient can browse clinics.
- Patient can book appointments.
- Patient can view their own appointments.
- Patient can view allowed records grouped by clinic.
- Clinic staff can manage their own clinic's appointments.
- Authorized clinical staff can create medical notes.
- Notifications are queued and sent.

### Security

- Organization isolation passes.
- Clinic isolation passes.
- Patient self-scope passes.
- Refresh token rotation passes.
- Audit logs are present.
- Secrets are outside source control.
- HTTPS is enabled.

### Quality

- Build succeeds.
- Unit tests pass.
- Integration tests pass.
- Database migrations are current.
- Swagger is current.
- No debug endpoints are enabled in production.
- Logs do not expose protected data.

---

## 19. Standard Cursor prompt for each phase

Use this prompt pattern:

```text
Read these files before making any change:
- docs/architecture.md
- docs/development-plan.md
- docs/security.md

Implement only Phase [NUMBER]: [PHASE NAME].

Before editing:
1. Summarize the requirements for this phase.
2. List the files you plan to create or modify.
3. Identify the authorization and data-isolation rules involved.

Implementation rules:
- Follow the approved modular monolith architecture.
- Use PostgreSQL and Npgsql only.
- Do not add microservices or unnecessary packages.
- Do not modify unrelated files.
- Use async database and network operations.
- Add FluentValidation.
- Add authorization policies.
- Add unit and integration tests.
- Include migrations when required.
- Use Problem Details for API errors.
- Do not expose EF Core entities directly.

After implementation:
1. Run dotnet format.
2. Run dotnet build.
3. Run all relevant tests.
4. Fix all failures.
5. Summarize the changes.
6. List any remaining risks or assumptions.
```

---

## 20. Development discipline

Cursor must not:

- Build all phases at once.
- Replace PostgreSQL with SQL Server.
- Add microservices.
- Add complex CQRS without approval.
- Add MediatR automatically.
- Add AutoMapper automatically.
- Add Redis automatically.
- Add Kafka or RabbitMQ.
- Add code that bypasses clinic isolation.
- Add controller actions without authorization review.
- Leave untested security-sensitive code.
- store secrets in source control.
