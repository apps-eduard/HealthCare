namespace HealthCare.Application.Authorization;

/// <summary>
/// Request-scoped authenticated identity and tenant scope.
/// Scope values are resolved from the JWT and validated against server-side records — never from the client.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Email { get; }

    IReadOnlyList<string> Roles { get; }

    Guid? OrganizationId { get; }

    Guid? ClinicId { get; }

    /// <summary>
    /// Linked patient id when available. Remains null until the Patient module links an account.
    /// </summary>
    Guid? PatientId { get; }

    Guid? StaffMemberId { get; }

    bool IsInRole(string role);
}
