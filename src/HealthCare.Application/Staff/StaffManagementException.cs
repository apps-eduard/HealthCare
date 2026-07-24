using HealthCare.Contracts.Staff;

namespace HealthCare.Application.Staff;

public sealed class StaffManagementException : Exception
{
    public StaffManagementException(string errorCode, string title, int statusCode = 409)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static StaffManagementException NotFound() =>
        new(StaffErrorCodes.NotFound, "Staff member was not found.", 404);

    public static StaffManagementException EmailInUse() =>
        new(StaffErrorCodes.EmailInUse, "A staff account with this email cannot be created.", 409);

    public static StaffManagementException InvalidRole() =>
        new(StaffErrorCodes.InvalidRole, "The requested role is invalid.", 400);

    public static StaffManagementException RoleAssignmentDenied() =>
        new(StaffErrorCodes.RoleAssignmentDenied, "Role assignment is not permitted.", 403);

    public static StaffManagementException SelfElevationDenied() =>
        new(StaffErrorCodes.SelfElevationDenied, "Self-elevation is not permitted.", 403);

    public static StaffManagementException CrossTenantDenied() =>
        new(StaffErrorCodes.CrossTenantDenied, "Cross-tenant staff access is denied.", 403);

    public static StaffManagementException InactiveOrganization() =>
        new(StaffErrorCodes.InactiveOrganization, "The organization is inactive.", 409);

    public static StaffManagementException InactiveClinic() =>
        new(StaffErrorCodes.InactiveClinic, "The clinic is inactive.", 409);

    public static StaffManagementException ConcurrencyConflict() =>
        new(StaffErrorCodes.ConcurrencyConflict, "The staff record was modified by another request.", 409);

    public static StaffManagementException LastAdminProtected() =>
        new(StaffErrorCodes.LastAdminProtected, "The last administrator in scope cannot be removed or deactivated.", 409);

    public static StaffManagementException ActivationNotAllowed() =>
        new(StaffErrorCodes.ActivationNotAllowed, "Staff activation is not allowed.", 409);

    public static StaffManagementException DeactivationNotAllowed() =>
        new(StaffErrorCodes.DeactivationNotAllowed, "Staff deactivation is not allowed.", 409);

    public static StaffManagementException SelfDeactivationDenied() =>
        new(StaffErrorCodes.SelfDeactivationDenied, "You cannot deactivate your own staff membership.", 403);

    public static StaffManagementException AlreadyActive() =>
        new(StaffErrorCodes.AlreadyActive, "The staff membership is already active.", 409);

    public static StaffManagementException AlreadyInactive() =>
        new(StaffErrorCodes.AlreadyInactive, "The staff membership is already inactive.", 409);

    public static StaffManagementException EmptyPatch() =>
        new(StaffErrorCodes.EmptyPatch, "No editable staff fields were supplied.", 400);

    public static StaffManagementException CreationFailed(string detail) =>
        new(StaffErrorCodes.CreationFailed, detail, 400);

    public static StaffManagementException ClinicChangeNotAllowed(string? detail = null) =>
        new(StaffErrorCodes.ClinicChangeNotAllowed, detail ?? "Clinic reassignment is not allowed for this staff member.", 409);

    public static StaffManagementException PasswordResetNotAllowed() =>
        new(StaffErrorCodes.PasswordResetNotAllowed, "Password reset is not allowed for this account.", 403);

    public static StaffManagementException PasswordResetFailed(string detail) =>
        new(StaffErrorCodes.PasswordResetFailed, detail, 400);

    public static StaffManagementException SessionRevocationFailed(string detail) =>
        new(StaffErrorCodes.SessionRevocationFailed, detail, 400);
}
