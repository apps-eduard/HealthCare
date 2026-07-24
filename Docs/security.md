# HealthCare MVP Security Requirements

## 1. Purpose

HealthCare handles sensitive patient and clinical information.

The most important security requirement is strict isolation between organizations, clinics, and patients.

This document is mandatory for all implementation work. Cursor must read it before generating or modifying authentication, authorization, data access, API, background job, logging, or UI code.

---

## 2. Security model summary

HealthCare uses three main access boundaries:

```text
Boundary 1: Organization isolation
Boundary 2: Clinic isolation
Boundary 3: Patient self-scope isolation
```

### Organization isolation

A user associated with one organization cannot access another organization's administrative data.

### Clinic isolation

Clinic staff can access only data owned by their assigned clinic.

### Patient self-scope isolation

A patient can access only data connected to their own global patient record.

These boundaries must be enforced independently.

---

## 3. Core privacy scenario

A patient has one global account and may use multiple clinics.

```text
Global Patient P-10001
├── Clinic A relationship
│   ├── Clinic A appointments
│   ├── Clinic A medical notes
│   └── Clinic A documents
└── Clinic B relationship
    ├── Clinic B appointments
    ├── Clinic B medical notes
    └── Clinic B documents
```

The patient can access their own permitted information from both clinics.

Clinic A must not access Clinic B's records.

Clinic B must not access Clinic A's records.

A clinic must not automatically see which other clinics the patient uses.

---

## 4. Roles

```text
PLATFORM_ADMIN
ORGANIZATION_ADMIN
CLINIC_ADMIN
DOCTOR
NURSE
RECEPTIONIST
PATIENT
```

### 4.1 PLATFORM_ADMIN

Allowed:

- Manage platform configuration.
- Browse the organization directory (`organizations.read`) for tenant selection.
- Select a tenant context in Staff Web (`organizations.select`) as a usability aid.
- Manage organizations (future create/update/suspend — not in current slice).
- Activate or deactivate organizations (future).
- View operational audit information.
- Cross-tenant staff/clinic/appointment/patient/availability operations only with **explicit** `platformAdminBypass=true` plus validated OrganizationId/ClinicId where required.

Not automatically allowed:

- Read medical note contents (no `medical_notes.*`; selected organization does not change this).
- Browse patient records without a support purpose and without explicit bypass + clinic scope.
- Impersonate a user without an explicit audited process.
- Treat organization selection as authorization — API remains authoritative.

### 4.2 ORGANIZATION_ADMIN

Allowed:

- Manage organization profile.
- Manage clinics under their organization.
- Manage organization-level staff assignments where authorized.

Not automatically allowed:

- Read all clinical notes.
- Access another organization.

### 4.3 CLINIC_ADMIN

Allowed:

- Manage their clinic profile.
- Manage staff in their clinic.
- Manage appointment operations.
- View clinic-level reports.

Not automatically allowed:

- Access another clinic.
- Read medical notes unless granted a clinical permission.

### 4.4 DOCTOR

Allowed:

- View patients registered with their clinic.
- View clinic appointments.
- Create and read clinical notes within their clinic.
- Update appointment status where permitted.

### 4.5 NURSE

Allowed:

- View clinic patients and appointments as permitted.
- Create or read nursing notes as permitted.

### 4.6 RECEPTIONIST

Allowed:

- Register patients with the clinic.
- Manage appointment scheduling.
- View required demographic information.

Not allowed by default:

- Read clinical note contents.
- Access another clinic.

### 4.7 PATIENT

Allowed:

- Manage their own profile.
- Discover clinics.
- Book appointments for themselves.
- View their own appointments.
- View only their own records that are marked visible to the patient.

Not allowed:

- Access another patient's data.
- Access staff endpoints.
- Select or manipulate another patient ID.

---

## 5. Authentication requirements

### 5.1 Patient authentication

- Support Google authentication.
- Validate Google identity tokens on the server.
- Store the external provider subject identifier.
- Do not trust only the email address as proof of identity.
- Link external login to an internal UUID user ID.
- Allow account recovery using a controlled workflow.

### 5.2 Staff authentication

- Use ASP.NET Core Identity.
- Require verified email where practical.
- Enforce account lockout after repeated failed attempts.
- Require strong passwords.
- Support password reset using time-limited tokens.

### 5.3 JWT access tokens

- Keep access token lifetime short.
- Validate issuer.
- Validate audience.
- Validate signature.
- Validate expiration.
- Validate token type.
- Include only necessary claims.
- Do not store sensitive medical data in claims.

### 5.4 Refresh tokens

- Generate cryptographically secure random tokens.
- Store only a hash of each refresh token.
- Rotate refresh tokens after every use.
- Track token family.
- Detect reuse of an old token.
- Revoke the full family after reuse detection.
- Revoke tokens on logout.
- Revoke tokens when an account is disabled.
- Revoke **all** refresh tokens for a user (and update the Identity security stamp) via `ISecuritySessionInvalidationService` after staff deactivation, role assignment, clinic reassignment, admin password-reset initiation, and explicit `POST .../revoke-sessions`. Never log token values or hashes.
- Staff web uses an HttpOnly BFF cookie plus server-side API token sessions (distributed cache). Access/refresh tokens are never stored in browser storage or returned to the browser.

### 5.4b Staff password reset (admin-initiated)

- Organization/Clinic admins with `staff.password_reset` may initiate reset for in-scope staff: `POST /api/v1/staff-management/staff/{id}/password-reset`.
- Uses Identity password-reset tokens delivered through `IAccountEmailSender` (Development stores tokens in memory for manual testing; production sender may be unconfigured).
- API responses are generic and never include the raw reset token.
- Completion: `POST /api/v1/auth/complete-password-reset` (anonymous) with email + token + new password; then sessions are invalidated.
- Development-only helper: `GET /api/v1/auth/dev/password-reset-token?email=` (404 outside Development).

### 5.4a Staff Web BFF authentication (hardened)

Staff UI authentication mutations are POST-only and antiforgery-protected:

| Endpoint | Method | Behavior |
|----------|--------|----------|
| `/bff/auth/login` | `POST` | Validates antiforgery, authenticates via API, creates a **new** server token session, issues auth cookie, redirects |
| `/bff/auth/logout` | `POST` | Validates antiforgery, revokes API refresh token (best effort), deletes server session, expires cookies |
| `/bff/auth/establish` | any | `405` — removed; login establishes the session in one step |
| `/bff/auth/logout` | `GET` | `405` — must not mutate auth state |

**Login CSRF:** There is no separate establish redirect or login-ticket cookie. Login is a single antiforgery-protected form POST from the victim browser; an attacker site cannot forge a valid antiforgery token for that browser. SameSite alone is not relied upon.

**Session fixation:** Before issuing a new session, any prior `bff_sid` session is removed and the auth cookie is signed out. A new high-entropy session id is always created on successful login.

**Session binding:** Cookie `NameIdentifier` must match the server session `UserId`. Mismatch deletes the session, clears the cookie, and logs `session_mismatch`.

**Cookies:**

- Production prefers `__Host-HealthCare.Staff` (Secure, Path=`/`, no Domain).
- Development over HTTP uses `HealthCare.Staff.Auth` with `Bff:RequireHttps=false`.
- Cookie is HttpOnly, SameSite=Lax, holds minimal claims + opaque `bff_sid` only (never API tokens).
- Legacy login-ticket / correlation cookies are deleted on login/logout if present.

**Logout UX:** UI navigates to `/logout`, which antiforgery-POSTs to `/bff/auth/logout` (no GET logout).

**Refresh / logout races:** Token refresh re-checks the session under a process-local lock and uses versioned `TryUpdateTokensAsync` so a late refresh cannot recreate a deleted session. Multi-instance deployments need a shared distributed cache and distributed refresh locks.

**Return URLs:** `SafeReturnUrl` accepts local paths only; rejects absolute, protocol-relative, backslash, encoded, and double-encoded external URLs (fallback `/dashboard`).

**Logging:** Auth events use safe reason codes. Never log passwords, tokens, cookies, session ids, tickets, or antiforgery values.

- Record creation, expiration, revocation, IP address, and user agent where appropriate.

### 5.5 Mobile token storage

- Use secure platform storage.
- Never store tokens in plain preferences or local text files.
- Clear all tokens on logout.
- Avoid caching sensitive API responses unnecessarily.

---

## 6. Authorization requirements

### 6.1 Never trust client-supplied scope

The API must never trust these values simply because the client provided them:

- OrganizationId
- ClinicId
- PatientId
- StaffMemberId
- Role
- Permission

Resolve scope from the authenticated identity and server-side records.

### 6.1a Fine-grained permissions (MVP)

Operations authorize through the code-defined catalog in `Permissions` / `RolePermissionMatrix`
(see [authorization-matrix.md](./authorization-matrix.md)). Permission grants capability only;
tenant isolation, patient self-scope, and explicit `PLATFORM_ADMIN` bypass remain mandatory.

- Never trust client-supplied permission claims.
- Resolve effective roles from DB Identity roles + active staff membership + patient linkage.
- Unknown permissions fail closed.
- Custom DB-editable role/permission editing is deferred (no permission tables in MVP).

### 6.2 Clinic staff rule

For clinic-owned data, access is allowed only when:

```text
Resource.ClinicId == CurrentStaff.ClinicId
```

### 6.3 Organization rule

For organization-owned administrative data, access is allowed only when:

```text
Resource.OrganizationId == CurrentStaff.OrganizationId
```

### 6.4 Patient self-scope rule

For patient data, access is allowed only when:

```text
Resource.ClinicPatient.PatientId == CurrentPatient.Id
```

### 6.5 Do not authorize using route IDs alone

Incorrect pattern:

```csharp
var appointment = await db.Appointments.FindAsync(id);
return appointment;
```

Required pattern:

```csharp
var appointment = await db.Appointments
    .Where(x => x.Id == id)
    .Where(x => x.ClinicId == currentStaff.ClinicId)
    .SingleOrDefaultAsync();
```

For a patient:

```csharp
var appointment = await db.Appointments
    .Where(x => x.Id == id)
    .Where(x => x.ClinicPatient.PatientId == currentPatient.Id)
    .SingleOrDefaultAsync();
```

### 6.6 Defense in depth

Isolation must be enforced using multiple layers:

- Authorization policies
- Current-user scope services
- Scoped application services
- Query predicates
- EF Core global filters where appropriate
- Database foreign keys
- Unique constraints
- Integration tests
- Audit logs

Do not rely on UI filtering.

---

## 7. Database security rules

### 7.1 Required scope columns

All clinic-owned tables must include `ClinicId` directly or through a mandatory relationship.

For MVP, prefer a direct `ClinicId` on these entities:

- StaffMember
- ClinicPatient
- Appointment
- MedicalNote
- AuditLog
- Notification

### 7.2 Required constraints

- Unique `(ClinicId, PatientId)` on `ClinicPatient`.
- Appointment clinic must match clinic-patient clinic.
- Appointment doctor must belong to the same clinic.
- Medical note clinic must match clinic-patient clinic.
- Foreign keys must restrict invalid relationships.
- Required relationships must not be nullable.

### 7.3 UUIDs

Use UUID primary keys to reduce predictable identifiers.

UUIDs do not replace authorization.

### 7.4 Timestamps

- Store timestamps in UTC.
- Use server-generated timestamps where practical.
- Track created and updated timestamps.

### 7.5 Sensitive data

Do not store:

- Plaintext passwords
- Plaintext refresh tokens
- Google access tokens unless strictly required
- Unencrypted application secrets

---

## 8. Medical record security

### 8.1 Default privacy

Medical notes are clinic-private clinical content. MVP APIs do **not** expose notes to patients.

Access requires:

- Active staff membership in the note’s clinic/organization
- Clinical role (`DOCTOR` or `NURSE`)
- Explicit `medical_notes.*` permission
- Administrative roles alone (clinic/org/platform admin, receptionist) **must not** read note bodies

### 8.2 Lifecycle and immutability

- Drafts: editable only by original author
- Signed notes: immutable; no ordinary delete
- Amendments: new signed rows linked via `AmendsMedicalNoteId`; original content preserved
- Audit events (`MedicalNoteAuditEvent`) and structured logs must never contain SOAP/amendment body text

### 8.3 Cross-clinic access

Clinic A staff must never read Clinic B notes. Out-of-scope requests return safe 404. PLATFORM_ADMIN bypass does **not** grant medical-note content.

### 8.4 Encryption and operational controls

MVP does not implement application-level field encryption for note bodies.

Required for production:

- TLS for all API traffic
- Encrypted PostgreSQL storage/volume and encrypted backups
- Restricted DB credentials and least-privilege app roles
- Secrets via secure configuration (not source control)

### 8.5 Future consent / patient visibility

Patient-visible notes and consent-based sharing are deferred. Do not partially implement automatic sharing in the MVP.

---

## 9. API security

### 9.1 HTTPS

- Production traffic must use HTTPS.
- Redirect HTTP to HTTPS.
- Use secure TLS configuration.

### 9.2 CORS

- Allow only known application origins.
- Do not use wildcard origins with credentials.
- Keep development and production policies separate.

### 9.3 Rate limiting

Apply rate limits to:

- Login
- Registration
- Google authentication exchange
- Password reset
- Refresh token endpoint
- Public clinic search where abuse is possible

### 9.4 Validation

- Validate all request bodies.
- Validate route values.
- Validate query parameters.
- Enforce maximum string lengths.
- Enforce pagination limits.
- Reject unexpected enum values.

### 9.5 Error handling

- Use Problem Details.
- Do not expose stack traces in production.
- Do not expose internal SQL or exception details.
- Do not reveal whether a user account exists during login or password reset.

### 9.6 Mass assignment

Use explicit DTOs.

Never bind API requests directly to EF Core entities.

### 9.7 File uploads

File upload is outside the initial MVP unless explicitly added later.

When added, require:

- File type validation
- Size limits
- Malware scanning
- Random storage names
- Access authorization
- Storage outside the web root

---

## 10. Web application security

- Use Ant Design Blazor only for presentation.
- The web UI must not be treated as a security boundary.
- Do not render controls based only on client-side role checks.
- API authorization remains mandatory.
- Use anti-forgery protection where cookie authentication is used.
- Do not place secrets in client-side code.
- Avoid storing access tokens in browser local storage when a safer server-mediated pattern is available.

---

## 11. Mobile application security

- Use platform secure storage for tokens.
- Do not hardcode API secrets.
- Do not include privileged service credentials in the app.
- Validate TLS certificates normally.
- Do not disable certificate validation.
- Avoid screenshots containing sensitive data where platform controls are practical.
- Clear sensitive local state on logout.

---

## 12. Hangfire security

- Hangfire dashboard must require authentication.
- Restrict dashboard access to approved administrators.
- Do not expose the dashboard publicly.
- Jobs must re-check authorization-relevant state.
- Jobs must not trust stale serialized user claims.
- Pass record IDs to jobs, then reload current data.
- Avoid logging full patient or medical content.
- Design jobs to avoid duplicate notifications.

---

## 13. Logging and audit

### 13.1 Security logging

Log:

- Login success and failure
- Account lockout
- Password reset request and completion
- Refresh token reuse detection
- Account activation and deactivation
- Role changes
- Staff assignment changes
- Appointment creation and status change
- Medical note creation
- Sensitive record access where required
- Administrative changes

### 13.2 Data that must not be logged

Never log:

- Passwords
- Password reset tokens
- Access tokens
- Refresh tokens
- Google identity tokens
- Connection strings
- Private keys
- Full medical note contents
- Full request bodies containing patient data

### 13.3 Audit integrity

- Audit records must be append-only for normal users.
- Include actor, action, entity, time, clinic, and organization where applicable.
- Use UTC.
- Record correlation ID.
- Audit administrative overrides.

---

## 14. Secrets management

Development:

- Use .NET user secrets or environment variables.
- Commit only `.env.example` with placeholders.

Production:

- Use environment variables or a secret manager.
- Do not commit secrets.
- Rotate compromised secrets.

Secrets include:

- Database password
- JWT signing key
- Google client secret
- Email provider credentials
- SMS provider credentials
- Hangfire dashboard credentials if used

---

## 15. Security headers

Configure appropriate headers:

- Strict-Transport-Security
- X-Content-Type-Options
- Content-Security-Policy where practical
- Referrer-Policy
- Frame restrictions

Do not add headers blindly. Verify compatibility with Blazor and Ant Design Blazor.

---

## 16. Security testing requirements

### 16.1 Organization isolation tests

- Organization A admin cannot read Organization B.
- Organization A admin cannot modify Organization B.
- Organization A clinic queries do not return Organization B clinics.

### 16.2 Clinic isolation tests

- Clinic A staff cannot read Clinic B patient relationships.
- Clinic A staff cannot read Clinic B appointments.
- Clinic A staff cannot create a note for Clinic B.
- Clinic A staff cannot update Clinic B appointment status.
- Manipulating `ClinicId` in the request does not bypass isolation.

### 16.3 Patient self-scope tests

- Patient A cannot read Patient B profile.
- Patient A cannot read Patient B appointments.
- Patient A cannot cancel Patient B appointment.
- Patient A cannot read Patient B records.
- Manipulating a route ID does not bypass self-scope.

### 16.4 Role tests

- Receptionist cannot read clinical note contents.
- Patient cannot call staff endpoints.
- Doctor cannot access platform administration.
- Disabled staff cannot access protected endpoints.

### 16.5 Token tests

- Expired access token fails.
- Revoked refresh token fails.
- Reused refresh token revokes token family.
- Disabled account token is rejected where current-state validation is required.

### 16.6 Input tests

- Overlong input is rejected.
- Invalid UUID is rejected.
- Invalid enum is rejected.
- Excessive page size is rejected.
- Malformed JSON returns a safe error.

---

## 17. Secure coding rules for Cursor

Cursor must:

1. Read this file before security-sensitive changes.
2. Identify the access boundary for every endpoint.
3. Add authorization at the API boundary.
4. Filter queries by server-resolved scope.
5. Add integration tests for cross-scope access.
6. Use explicit DTOs.
7. Validate all inputs.
8. Avoid logging sensitive values.
9. Use secure defaults.
10. Include migrations and constraints.

Cursor must not:

- Trust `ClinicId` from the client.
- Trust `PatientId` from the client for patient self-service.
- Use email as the authorization key.
- Return unfiltered `DbSet` results.
- Disable authorization temporarily.
- Add `AllowAnonymous` without explicit justification.
- Store refresh tokens in plaintext.
- expose stack traces in production.
- log complete medical records.
- create a generic repository that hides security filters.

---

## 18. Endpoint security checklist

Before declaring any endpoint complete, verify:

- Who can call it?
- Which organization owns the data?
- Which clinic owns the data?
- Is the caller a patient?
- If patient, is the data linked to their own `PatientId`?
- Are request IDs treated as untrusted?
- Is the database query scope-filtered?
- Is the request validated?
- Is the action audited?
- Does the response omit unnecessary sensitive fields?
- Is there an integration test for unauthorized access?

---

## 19. Incident readiness for MVP

The MVP should support:

- Account disabling
- Refresh token revocation
- Audit log review
- Password reset
- Secret rotation
- Database backup restoration
- Identification of affected users and clinics through audit records

---

## 20. Security definition of done

A security-sensitive feature is complete only when:

- Authentication is implemented correctly.
- Authorization policy is explicit.
- Organization scope is enforced where relevant.
- Clinic scope is enforced where relevant.
- Patient self-scope is enforced where relevant.
- Database relationships are constrained.
- Negative authorization tests exist.
- Sensitive values are not logged.
- Errors are safe for production.
- Audit events are recorded.
- Build and security-related tests pass.
