using HealthCare.Contracts.Doctors;
using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.DoctorProfile;

public static class DoctorProfilePermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.DoctorProfileRead);

    public static bool CanUpdate(IPermissionState permissions) =>
        permissions.Has(WebPermissions.DoctorProfileUpdate);
}

public static class DoctorProfilePresentation
{
    public static StatusTone StatusTone(bool isActive) =>
        isActive ? Design.StatusTone.Success : Design.StatusTone.Neutral;

    public static string StatusText(bool isActive) => isActive ? "Active" : "Inactive";

    public static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    public static string ResolveDisplayName(DoctorProfileResponse profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return profile.DisplayName.Trim();
        }

        var combined = $"{profile.FirstName} {profile.LastName}".Trim();
        return string.IsNullOrWhiteSpace(combined) ? "Doctor" : combined;
    }
}

public sealed class DoctorProfileFormState
{
    public string? DisplayName { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? JobTitle { get; set; }

    public string? ContactPhone { get; set; }

    public int ExpectedVersion { get; set; }

    public static DoctorProfileFormState FromResponse(DoctorProfileResponse response) =>
        new()
        {
            DisplayName = response.DisplayName,
            FirstName = response.FirstName,
            LastName = response.LastName,
            JobTitle = response.JobTitle,
            ContactPhone = response.ContactPhone,
            ExpectedVersion = response.Version,
        };

    /// <summary>
    /// Client validation mirroring backend rules (not stricter).
    /// Returns null when valid; otherwise a user-facing message.
    /// </summary>
    public string? ValidateForUpdate()
    {
        if (string.IsNullOrWhiteSpace(FirstName))
        {
            return "First name is required.";
        }

        if (FirstName.Trim().Length > 100)
        {
            return "First name must be at most 100 characters.";
        }

        if (string.IsNullOrWhiteSpace(LastName))
        {
            return "Last name is required.";
        }

        if (LastName.Trim().Length > 100)
        {
            return "Last name must be at most 100 characters.";
        }

        if (!string.IsNullOrWhiteSpace(DisplayName) && DisplayName.Trim().Length > 200)
        {
            return "Display name must be at most 200 characters.";
        }

        if (!string.IsNullOrWhiteSpace(JobTitle) && JobTitle.Trim().Length > 150)
        {
            return "Job title must be at most 150 characters.";
        }

        if (!string.IsNullOrWhiteSpace(ContactPhone) && ContactPhone.Trim().Length > 30)
        {
            return "Contact phone must be at most 30 characters.";
        }

        return null;
    }

    public UpdateDoctorProfileRequest? TryBuildUpdateRequest(out string? validationError)
    {
        validationError = ValidateForUpdate();
        if (validationError is not null)
        {
            return null;
        }

        return new UpdateDoctorProfileRequest
        {
            ExpectedVersion = ExpectedVersion,
            DisplayName = NormalizeOptional(DisplayName),
            FirstName = FirstName.Trim(),
            LastName = LastName.Trim(),
            JobTitle = NormalizeOptional(JobTitle),
            ContactPhone = NormalizeOptional(ContactPhone),
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class DoctorProfileProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            DoctorProfileErrorCodes.AccessDenied =>
                "You do not have permission to access doctor profile.",
            DoctorProfileErrorCodes.InvalidScope =>
                "The selected doctor profile scope is invalid.",
            DoctorProfileErrorCodes.ClinicScopeRequired =>
                "Select a clinic before loading doctor profile.",
            DoctorProfileErrorCodes.DoctorScopeRequired =>
                "Select a doctor before loading doctor profile.",
            DoctorProfileErrorCodes.ClinicNotFound =>
                "Clinic was not found.",
            DoctorProfileErrorCodes.DoctorNotFound =>
                "Doctor was not found.",
            DoctorProfileErrorCodes.EmptyUpdate =>
                "Provide at least one doctor profile field to update.",
            DoctorProfileErrorCodes.InvalidField =>
                string.IsNullOrWhiteSpace(ex.Title) ? "One or more profile fields are invalid." : ex.Title,
            DoctorProfileErrorCodes.ConcurrencyConflict =>
                "Another change was saved first. Reload the latest profile and try again.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            _ => ex.StatusCode switch
            {
                401 => "Sign in to view doctor profile.",
                403 => "You do not have permission to access doctor profile.",
                404 => "Doctor profile was not found.",
                409 => "Another change was saved first. Reload the latest profile and try again.",
                _ => string.IsNullOrWhiteSpace(ex.Title)
                    ? "Unable to load doctor profile."
                    : ex.Title,
            },
        };
    }
}
