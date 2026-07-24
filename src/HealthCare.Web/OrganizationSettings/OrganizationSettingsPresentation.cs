using System.Net.Mail;
using System.Text.RegularExpressions;
using HealthCare.Contracts.Organizations;
using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.OrganizationSettings;

public static class OrganizationSettingsPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.OrganizationProfileRead);

    public static bool CanUpdate(IPermissionState permissions) =>
        permissions.Has(WebPermissions.OrganizationProfileUpdate);
}

public static class OrganizationSettingsPresentation
{
    public static StatusTone StatusTone(string? status) =>
        status?.Trim() switch
        {
            "Active" => Design.StatusTone.Success,
            "Suspended" => Design.StatusTone.Error,
            "Inactive" => Design.StatusTone.Neutral,
            _ => Design.StatusTone.Info,
        };

    public static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}

public sealed class OrganizationSettingsFormState
{
    public string Name { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? Country { get; set; }

    public string? DefaultTimeZoneId { get; set; }

    public string? BrandingPlaceholder { get; set; }

    public int ExpectedVersion { get; set; }

    public static OrganizationSettingsFormState FromResponse(OrganizationSettingsResponse response) =>
        new()
        {
            Name = response.Name,
            ContactEmail = response.ContactEmail,
            ContactPhone = response.ContactPhone,
            Country = response.Country,
            DefaultTimeZoneId = response.DefaultTimeZoneId,
            BrandingPlaceholder = response.BrandingPlaceholder,
            ExpectedVersion = response.Version,
        };

    /// <summary>
    /// Client validation mirroring <see cref="UpdateOrganizationSettingsRequest"/> rules.
    /// Returns null when valid; otherwise a user-facing message.
    /// </summary>
    public string? ValidateForUpdate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Organization name is required.";
        }

        if (Name.Trim().Length > 200)
        {
            return "Organization name must be at most 200 characters.";
        }

        if (!string.IsNullOrWhiteSpace(ContactEmail))
        {
            if (ContactEmail.Trim().Length > 256)
            {
                return "Contact email must be at most 256 characters.";
            }

            if (!LooksLikeEmail(ContactEmail.Trim()))
            {
                return "Contact email format is invalid.";
            }
        }

        if (!string.IsNullOrWhiteSpace(ContactPhone) && ContactPhone.Trim().Length > 32)
        {
            return "Contact phone must be at most 32 characters.";
        }

        if (!string.IsNullOrWhiteSpace(Country) && Country.Trim().Length > 100)
        {
            return "Country must be at most 100 characters.";
        }

        if (!string.IsNullOrWhiteSpace(DefaultTimeZoneId) && DefaultTimeZoneId.Trim().Length > 100)
        {
            return "Default timezone must be at most 100 characters.";
        }

        if (!string.IsNullOrWhiteSpace(BrandingPlaceholder) && BrandingPlaceholder.Trim().Length > 200)
        {
            return "Branding placeholder must be at most 200 characters.";
        }

        return null;
    }

    public UpdateOrganizationSettingsRequest? TryBuildUpdateRequest(out string? validationError)
    {
        validationError = ValidateForUpdate();
        if (validationError is not null)
        {
            return null;
        }

        return new UpdateOrganizationSettingsRequest
        {
            ExpectedVersion = ExpectedVersion,
            Name = Name.Trim(),
            ContactEmail = NormalizeOptional(ContactEmail),
            ContactPhone = NormalizeOptional(ContactPhone),
            Country = NormalizeOptional(Country),
            DefaultTimeZoneId = NormalizeOptional(DefaultTimeZoneId),
            BrandingPlaceholder = NormalizeOptional(BrandingPlaceholder),
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool LooksLikeEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase)
                   && Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }
        catch
        {
            return false;
        }
    }
}

public static class OrganizationSettingsProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            OrganizationSettingsErrorCodes.AccessDenied =>
                "You do not have permission to access organization profile settings.",
            OrganizationSettingsErrorCodes.InvalidScope =>
                "The selected organization profile scope is invalid.",
            OrganizationSettingsErrorCodes.OrganizationScopeRequired =>
                "Select an organization before loading profile settings.",
            OrganizationSettingsErrorCodes.OrganizationNotFound =>
                "Organization was not found.",
            OrganizationSettingsErrorCodes.EmptyUpdate =>
                "Provide at least one organization profile field to update.",
            OrganizationSettingsErrorCodes.InvalidTimezone =>
                "Default timezone is invalid. Use a valid IANA identifier.",
            OrganizationSettingsErrorCodes.ConcurrencyConflict =>
                "Another change was saved first. Reload the latest profile and try again.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            _ => ex.StatusCode switch
            {
                401 => "Sign in to view organization profile settings.",
                403 => "You do not have permission to access organization profile settings.",
                404 => "Organization was not found.",
                409 => "Another change was saved first. Reload the latest profile and try again.",
                _ => ex.ToUserMessage(),
            },
        };
    }
}
