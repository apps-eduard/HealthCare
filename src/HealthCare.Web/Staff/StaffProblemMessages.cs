using HealthCare.Contracts.Staff;
using HealthCare.Web.Services;

namespace HealthCare.Web.Staff;

public static class StaffProblemMessages
{
    public static string From(ApiProblemException ex) =>
        ex.ErrorCode switch
        {
            StaffErrorCodes.ConcurrencyConflict =>
                "Another change was saved first. Reload and try again.",
            StaffErrorCodes.LastAdminProtected =>
                "The last administrator in scope is protected.",
            StaffErrorCodes.SelfDeactivationDenied =>
                "You cannot deactivate your own staff membership.",
            StaffErrorCodes.SelfElevationDenied =>
                "You cannot elevate your own role.",
            StaffErrorCodes.RoleAssignmentDenied =>
                "That role assignment is not permitted.",
            StaffErrorCodes.ClinicChangeNotAllowed =>
                "Clinic reassignment is not allowed for this staff member.",
            StaffErrorCodes.LimitReached =>
                "The organization staff limit has been reached.",
            StaffErrorCodes.EmailInUse =>
                "A staff account with this email cannot be created.",
            StaffErrorCodes.InactiveClinic =>
                "The clinic is inactive.",
            StaffErrorCodes.InactiveOrganization =>
                "The organization is inactive.",
            StaffErrorCodes.InvalidClinic =>
                "The clinic is invalid for this operation.",
            StaffErrorCodes.PasswordResetNotAllowed =>
                "Password reset is not allowed for this account.",
            StaffErrorCodes.NotFound =>
                "Staff member was not found or is unavailable.",
            StaffErrorCodes.AlreadyActive =>
                "The staff membership is already active.",
            StaffErrorCodes.AlreadyInactive =>
                "The staff membership is already inactive.",
            StaffErrorCodes.ActivationNotAllowed =>
                "Staff activation is not allowed.",
            StaffErrorCodes.DeactivationNotAllowed =>
                "Staff deactivation is not allowed.",
            StaffErrorCodes.EmptyPatch =>
                "No editable staff fields were supplied.",
            StaffErrorCodes.InvalidRole =>
                "The requested role is invalid.",
            StaffErrorCodes.CreationFailed =>
                string.IsNullOrWhiteSpace(ex.Detail) ? "Staff creation failed." : ex.Detail!,
            StaffErrorCodes.PasswordResetFailed =>
                string.IsNullOrWhiteSpace(ex.Detail) ? "Password reset failed." : ex.Detail!,
            StaffErrorCodes.SessionRevocationFailed =>
                string.IsNullOrWhiteSpace(ex.Detail) ? "Session revocation failed." : ex.Detail!,
            StaffErrorCodes.CrossOrganizationDenied or StaffErrorCodes.CrossTenantDenied =>
                "Cross-organization staff access is denied.",
            _ => ex.ToUserMessage(),
        };
}
