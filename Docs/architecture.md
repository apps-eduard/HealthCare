# HealthCare MVP Architecture

## 1. Purpose

HealthCare is a multi-clinic healthcare platform where a patient creates one global account and can discover and book appointments with multiple independent clinics or hospitals.

The platform must provide a unified patient experience while maintaining strict privacy between clinics.

Example:

- The patient registers once using Google or email.
- The patient can see Clinic A, Clinic B, and other available clinics.
- The patient can book a dental appointment at Clinic A.
- The same patient can later book dialysis treatment at Clinic B.
- The patient can see their own records from both clinics.
- Clinic A must never see Clinic B's private records.
- Clinic B must never see Clinic A's private records.

This document defines the MVP architecture and the rules Cursor must follow when generating code.

---

## 2. Architecture principles

The MVP must follow these principles:

1. Use a modular monolith, not microservices.
2. Use one ASP.NET Core API.
3. Use one PostgreSQL database.
4. Keep modules logically separated.
5. Use one global patient identity.
6. Create a separate clinic-patient relationship for each clinic.
7. Store `ClinicId` on every clinic-owned record.
8. Enforce clinic isolation in application logic, authorization, tests, and database constraints.
9. Enforce patient self-scope so a patient can access only records linked to their own patient account.
10. Prefer simple, common, well-documented .NET patterns because Cursor will perform most implementation work.
11. Do not introduce unnecessary technologies during the MVP.

---

## 3. High-level system diagram

```text
                               USERS
                                 |
             +-------------------+-------------------+
             |                                       |
       Staff Browser                         Patient Mobile App
       Blazor Web App                        .NET MAUI Blazor Hybrid
       Fluent UI Blazor                       Android first
             |                                       |
             +-------------------+-------------------+
                                 |
                               HTTPS
                                 |
                       ASP.NET Core 10 API
                                 |
        +------------------------+------------------------+
        |                        |                        |
 Authentication           Business Modules         Background Jobs
 ASP.NET Core Identity     Organizations            Hangfire
 JWT / Refresh Tokens      Clinics                  Notifications
 Google Authentication     Staff                    Reminders
 Roles and Permissions     Patients                 Scheduled Reports
                           Appointments              Retry Failed Jobs
                           Medical Notes
                           Audit Logs
        |                        |
        +------------------------+
                                 |
                       Entity Framework Core
                                 |
                            PostgreSQL
```

---

## 4. Technology stack

### 4.1 Staff web application

- Blazor Web App
- ASP.NET Core 10
- Microsoft Fluent UI Blazor
- C#
- Responsive desktop-first design
- Support tablet layouts where practical
- HttpOnly BFF cookie authentication (`POST /bff/auth/login` / `POST /bff/auth/logout` only; antiforgery required)
- API access/refresh tokens stored server-side only (never in the browser)
- Staff UI: **Microsoft Fluent UI Blazor** (`Microsoft.FluentUI.AspNetCore.Components` 4.14.3) — see [fluent-ui-design-system.md](./fluent-ui-design-system.md)

### 4.2 Patient mobile application

- .NET MAUI Blazor Hybrid
- Android first
- iOS later
- Shared Razor components where practical
- Secure token storage
- REST API communication

### 4.3 Backend

- ASP.NET Core 10 Web API
- C#
- REST API
- Modular monolith architecture
- OpenAPI / Swagger
- FluentValidation
- Serilog

### 4.4 Authentication and authorization

- ASP.NET Core Identity
- Google authentication for patient registration and login
- Email/password as an optional second login method
- JWT access tokens
- Refresh tokens
- Role-based and policy-based authorization
- Clinic scope authorization
- Patient self-scope authorization

### 4.5 Database

- PostgreSQL
- Entity Framework Core
- Npgsql provider
- EF Core migrations
- UUID primary keys
- UTC timestamps

The MVP uses PostgreSQL. Do not generate SQL Server-specific code, migrations, packages, or configuration.

### 4.6 Background processing

- Hangfire
- Appointment reminders
- Confirmation email and SMS jobs
- Scheduled reports
- Retry failed notification jobs
- Cleanup of expired refresh tokens

### 4.7 Testing

- xUnit
- FluentAssertions
- Testcontainers for PostgreSQL integration tests
- ASP.NET Core integration testing
- Architecture tests where useful

### 4.8 Deployment

- Docker
- Docker Compose
- Ubuntu Server
- Nginx reverse proxy
- HTTPS with Let's Encrypt
- Git and GitHub

### 4.9 Development tools

- Cursor as the primary AI development environment
- Visual Studio as an optional secondary IDE
- Postman or Bruno
- DBeaver or pgAdmin

---

## 5. MVP scope

### 5.1 Included in MVP

- Global patient registration and login
- Google authentication
- Staff authentication
- Organization management
- Clinic management
- Staff management
- Clinic directory for patients
- Clinic profile and specialty
- Doctor availability
- Patient profile
- Clinic-patient registration relationship
- Appointment booking
- Appointment approval or confirmation
- Appointment cancellation
- Appointment status tracking
- Basic clinic-private medical notes
- Patient view of their own records
- Appointment confirmation notifications
- Appointment reminder notifications
- Audit logging
- Organization and clinic isolation
- Patient self-scope isolation
- Docker-based local and production deployment

### 5.2 Excluded from MVP

- Insurance claims
- Pharmacy integration
- Laboratory integration
- Payments
- Video consultation
- Advanced electronic medical record features
- Cross-clinic automatic record sharing
- Patient consent-based record sharing
- AI diagnosis or medical recommendations
- Complex hospital admission management
- Bed management
- Inventory management
- Microservices
- Kubernetes
- Kafka
- RabbitMQ
- Event sourcing
- Complex CQRS

---

## 6. Domain model

### 6.1 Global identity model

A user is a global platform identity.

```text
ApplicationUser
├── Id
├── Email
├── NormalizedEmail
├── PhoneNumber
├── IsActive
├── CreatedAtUtc
└── UpdatedAtUtc
```

A patient account is linked to one global user.

```text
Patient
├── Id
├── UserId
├── FirstName
├── MiddleName
├── LastName
├── DateOfBirth
├── Gender
├── MobileNumber
├── PreferredLanguage
├── Address
├── EmergencyContact
├── CreatedAtUtc
└── UpdatedAtUtc
```

Do not use email as a primary key. Use UUIDs.

### 6.2 Organization and clinic model

An organization owns one or more clinics.

```text
Organization
├── Id
├── Name
├── Slug
├── Status
├── CreatedAtUtc
└── UpdatedAtUtc

Clinic
├── Id
├── OrganizationId
├── Name
├── Slug
├── Specialty
├── Description
├── Address
├── City
├── PhoneNumber
├── Email
├── IsActive
├── CreatedAtUtc
└── UpdatedAtUtc
```

Even if the MVP starts with one clinic per organization, `Clinic` must remain a separate entity so the system is ready for multiple branches.

### 6.3 Clinic-patient relationship

The `ClinicPatient` entity links one global patient to one clinic.

```text
ClinicPatient
├── Id
├── ClinicId
├── PatientId
├── LocalPatientNumber
├── Status
├── RegisteredAtUtc
└── UpdatedAtUtc
```

Rules:

- One patient can have many `ClinicPatient` records.
- One clinic can have many `ClinicPatient` records.
- A unique constraint must exist on `(ClinicId, PatientId)`.
- A patient must not be registered twice in the same clinic.
- Clinic-specific records reference `ClinicPatientId`.

### 6.4 Staff model

```text
StaffMember
├── Id
├── UserId
├── OrganizationId
├── ClinicId
├── Role
├── JobTitle
├── IsActive
├── CreatedAtUtc
└── UpdatedAtUtc
```

MVP rule:

- A clinic staff member belongs to one clinic.
- Multi-clinic staff assignment can be added later through a separate assignment table.

### 6.5 Appointment model

```text
Appointment
├── Id
├── ClinicId
├── ClinicPatientId
├── DoctorStaffMemberId
├── AppointmentDateUtc
├── DurationMinutes
├── Reason
├── Status
├── PatientNotes
├── CancellationReason
├── CreatedByUserId
├── CreatedAtUtc
└── UpdatedAtUtc
```

Recommended statuses:

```text
Requested
Confirmed
CheckedIn
InProgress
Completed
CancelledByPatient
CancelledByClinic
NoShow
```

### 6.6 Medical note model

```text
MedicalNote
├── Id
├── OrganizationId / ClinicId / PatientId / ClinicPatientId / AppointmentId
├── AuthorStaffMemberId / AuthorUserId
├── NoteType (Progress|Consultation|Nursing|FollowUp|Procedure)
├── Status (Draft|Signed)
├── Subjective / Objective / Assessment / Plan / AdditionalText (plain text)
├── SignedAtUtc / SignedByStaffMemberId
├── Version (optimistic concurrency)
├── AmendsMedicalNoteId / AmendmentReason
├── CreatedAtUtc / UpdatedAtUtc
```

Medical notes are clinic-private clinical content. MVP has **no patient self-access** and **no ordinary delete**.
Signed notes are immutable; corrections create signed amendment rows.
Access requires clinical role (DOCTOR/NURSE) plus medical_notes.* permissions — administrative roles alone do not read note bodies.
Audit: `MedicalNoteAuditEvent` stores metadata only (never SOAP content).
Operational requirement: encrypt database/backups at rest; TLS in transit; no custom field encryption in MVP.

### 6.7 Audit log model

```text
AuditLog
├── Id
├── OrganizationId
├── ClinicId
├── UserId
├── Action
├── EntityType
├── EntityId
├── IpAddress
├── UserAgent
├── MetadataJson
└── CreatedAtUtc
```

Audit logs must be append-only from normal application workflows.

---

## 7. Module boundaries

Use these modules inside the modular monolith:

```text
HealthCare
├── Identity
├── Organizations
├── Clinics
├── Staff
├── Patients
├── Appointments
├── MedicalRecords
├── Notifications
└── Auditing
```

Each module should contain its own:

- Domain entities
- Application services or use cases
- DTOs
- Validators
- Authorization rules
- EF Core configurations
- API endpoints or controllers
- Unit tests
- Integration tests

Avoid direct coupling between modules where possible.

---

## 8. Solution structure

```text
HealthCare/
├── src/
│   ├── HealthCare.Api/
│   ├── HealthCare.Web/
│   ├── HealthCare.Mobile/
│   ├── HealthCare.Domain/
│   ├── HealthCare.Application/
│   ├── HealthCare.Infrastructure/
│   └── HealthCare.Contracts/
├── tests/
│   ├── HealthCare.UnitTests/
│   ├── HealthCare.IntegrationTests/
│   └── HealthCare.ArchitectureTests/
├── docs/
│   ├── architecture.md
│   ├── development-plan.md
│   └── security.md
├── docker-compose.yml
├── .env.example
├── Directory.Build.props
├── HealthCare.sln
└── README.md
```

For the MVP, a layered modular monolith is preferred over separate module projects for every module. Do not over-fragment the solution.

---

## 9. API conventions

- Use `/api/v1/...` routes.
- Use request and response DTOs.
- Never expose EF Core entities directly.
- Use standard HTTP status codes.
- Use Problem Details for errors.
- Use pagination for list endpoints.
- Use UTC for API dates.
- Return correlation IDs for traceability.
- Validate all requests using FluentValidation.

Example routes:

```text
POST   /api/v1/auth/google
POST   /api/v1/auth/login
POST   /api/v1/auth/refresh
POST   /api/v1/auth/logout
GET    /api/v1/clinics
GET    /api/v1/clinics/{clinicId}
GET    /api/v1/clinics/{clinicId}/doctors
POST   /api/v1/appointments
GET    /api/v1/patient/appointments
GET    /api/v1/patient/records
GET    /api/v1/staff/appointments
PATCH  /api/v1/staff/appointments/{id}/status
POST   /api/v1/staff/patients/{clinicPatientId}/medical-notes
```

---

## 10. Authentication flows

### 10.1 Patient Google login

```text
Patient selects Sign in with Google
        |
Google authenticates the patient
        |
API validates the Google identity token
        |
API finds or creates ApplicationUser
        |
API finds or creates Patient
        |
API issues access and refresh tokens
```

### 10.2 Staff login

```text
Staff opens /login (GET — display only)
        |
Staff submits email/password + antiforgery token
        |
POST /bff/auth/login (Web BFF)
        |
Web validates antiforgery; rejects missing/invalid tokens
        |
Web discards any prior BFF session (session fixation defense)
        |
Web calls API POST /api/v1/auth/login (server-to-server)
        |
API validates Identity credentials and issues access + refresh tokens
        |
Web creates a new server token session (opaque bff_sid)
        |
Web issues HttpOnly auth cookie (minimal claims + bff_sid)
        |
Browser redirects to safe local returnUrl (default /dashboard)
```

Patient-only accounts may authenticate but are redirected to `/forbidden` for staff pages.

### 10.2a Staff logout

```text
Staff chooses Sign out → navigates to /logout
        |
/logout page antiforgery-POSTs to /bff/auth/logout
        |
Web deletes server token session, clears cookie + tenant/permission state
        |
Web best-effort revokes API refresh token
        |
Redirect to /login (idempotent)
```

GET `/bff/auth/logout` and GET `/bff/auth/establish` return 405 and do not mutate authentication.

### 10.3 Token policy

- Access tokens must be short-lived.
- Refresh tokens must be stored hashed.
- Refresh token rotation is required.
- Reuse of an old refresh token must revoke the token family.
- Mobile tokens must be stored using secure platform storage.

---

## 11. Data isolation model

### 11.1 Patient access

A patient can access records only when:

```text
Record.ClinicPatient.PatientId == CurrentPatient.Id
```

### 11.2 Clinic staff access

Clinic staff can access records only when:

```text
Record.ClinicId == CurrentStaff.ClinicId
```

### 11.3 Organization admin access

Organization administrators can manage clinics and staff within their organization.

They do not automatically receive permission to read all clinical notes.

### 11.4 Platform admin access

Platform administrators manage platform configuration and organizations.

They must not receive routine unrestricted medical record access.

Any emergency support access must be explicit and audited.

---

## 12. UI requirements

### 12.1 Staff web application

Use Fluent UI Blazor to build:

- Dashboard
- Left navigation drawer
- Top application bar
- Appointment calendar
- Appointment queue
- Patient directory
- Patient profile
- Medical notes screen
- Staff management
- Clinic settings
- Audit log viewer
- Responsive forms and dialogs

### 12.2 Patient mobile application

The patient app should provide:

- Google sign-in
- Clinic discovery
- Search by clinic name, location, and specialty
- Clinic details
- Available doctors
- Appointment booking
- My appointments
- Appointment cancellation
- My clinic records grouped by clinic
- Profile management
- Notifications

Records must be grouped by clinic to avoid confusion.

---

## 13. Background jobs

Use Hangfire for work that must not delay API responses.

MVP jobs:

- Appointment confirmation email
- Appointment confirmation SMS
- Appointment reminder
- Daily clinic appointment summary
- Failed notification retry
- Expired refresh token cleanup

Rules:

- Main booking logic must complete in the API transaction first.
- Background jobs must use record IDs, not large serialized objects.
- Jobs must be idempotent where possible.
- Jobs must re-check current appointment status before sending reminders.
- Failed jobs must be visible in the Hangfire dashboard.
- The Hangfire dashboard must not be publicly accessible.

---

## 14. Logging and observability

Use Serilog with structured logging.

Include:

- Correlation ID
- User ID
- Organization ID where applicable
- Clinic ID where applicable
- Request path
- HTTP status code
- Duration

Never log:

- Passwords
- Access tokens
- Refresh tokens
- Google tokens
- Full medical note contents
- Sensitive patient data unless strictly necessary

---

## 15. Cursor implementation rules

Cursor must follow these rules for every task:

1. Read `architecture.md`, `development-plan.md`, and `security.md` first.
2. Explain the files it intends to modify.
3. Make only changes required for the current phase.
4. Do not redesign approved architecture without explicit instruction.
5. Do not change PostgreSQL to SQL Server.
6. Do not introduce microservices.
7. Do not add unnecessary packages.
8. Use async APIs for database and network operations.
9. Add validation.
10. Add authorization.
11. Add or update tests.
12. Run formatting, build, and tests.
13. Fix failures before stopping.
14. Update documentation when architecture or behavior changes.
15. Never leave placeholder security logic such as `TODO: authorize later`.

---

## 16. Definition of done

A feature is complete only when:

- Code compiles.
- Relevant unit tests pass.
- Relevant integration tests pass.
- Authorization is implemented.
- Organization and clinic isolation are tested.
- Patient self-scope is tested where applicable.
- Validation is implemented.
- Errors use Problem Details.
- Logging is appropriate and does not expose secrets.
- Database migration is included when needed.
- Swagger documentation is updated.
- No unrelated files were changed.
- Documentation is updated when needed.
