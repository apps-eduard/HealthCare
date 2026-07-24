using System.Net.Mail;
using System.Text.RegularExpressions;
using HealthCare.Contracts.Clinics;
using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.ClinicSettings;

public static class ClinicSettingsPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.ClinicProfileRead);

    public static bool CanUpdate(IPermissionState permissions) =>
        permissions.Has(WebPermissions.ClinicProfileUpdate);
}

public static class ClinicSettingsPresentation
{
    public static StatusTone StatusTone(bool isActive) =>
        isActive ? Design.StatusTone.Success : Design.StatusTone.Neutral;

    public static string StatusText(bool isActive) => isActive ? "Active" : "Inactive";

    public static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}

public sealed class ClinicSettingsFormState
{
    public string Name { get; set; } = string.Empty;

    public string? Specialty { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? DefaultTimeZoneId { get; set; }

    public int ExpectedVersion { get; set; }

    public static ClinicSettingsFormState FromResponse(ClinicSettingsResponse response) =>
        new()
        {
            Name = response.Name,
            Specialty = response.Specialty,
            ContactEmail = response.ContactEmail,
            ContactPhone = response.ContactPhone,
            Address = response.Address,
            City = response.City,
            Country = response.Country,
            DefaultTimeZoneId = response.DefaultTimeZoneId,
            ExpectedVersion = response.Version,
        };

    /// <summary>
    /// Client validation mirroring backend rules (not stricter).
    /// Returns null when valid; otherwise a user-facing message.
    /// </summary>
    public string? ValidateForUpdate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Clinic name is required.";
        }

        if (Name.Trim().Length > 200)
        {
            return "Clinic name must be at most 200 characters.";
        }

        if (!string.IsNullOrWhiteSpace(Specialty) && Specialty.Trim().Length > 150)
        {
            return "Specialty must be at most 150 characters.";
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

        if (!string.IsNullOrWhiteSpace(ContactPhone) && ContactPhone.Trim().Length > 50)
        {
            return "Contact phone must be at most 50 characters.";
        }

        if (!string.IsNullOrWhiteSpace(Address) && Address.Trim().Length > 200)
        {
            return "Address must be at most 200 characters.";
        }

        if (!string.IsNullOrWhiteSpace(City) && City.Trim().Length > 100)
        {
            return "City must be at most 100 characters.";
        }

        if (!string.IsNullOrWhiteSpace(Country) && Country.Trim().Length > 100)
        {
            return "Country must be at most 100 characters.";
        }

        if (string.IsNullOrWhiteSpace(DefaultTimeZoneId))
        {
            return "Default timezone is required.";
        }

        if (DefaultTimeZoneId.Trim().Length > 64)
        {
            return "Default timezone must be at most 64 characters.";
        }

        return null;
    }

    public UpdateClinicSettingsRequest? TryBuildUpdateRequest(out string? validationError)
    {
        validationError = ValidateForUpdate();
        if (validationError is not null)
        {
            return null;
        }

        return new UpdateClinicSettingsRequest
        {
            ExpectedVersion = ExpectedVersion,
            Name = Name.Trim(),
            Specialty = NormalizeOptional(Specialty),
            ContactEmail = NormalizeOptional(ContactEmail),
            ContactPhone = NormalizeOptional(ContactPhone),
            Address = NormalizeOptional(Address),
            City = NormalizeOptional(City),
            Country = NormalizeOptional(Country),
            DefaultTimeZoneId = DefaultTimeZoneId!.Trim(),
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

public static class ClinicSettingsProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            ClinicSettingsErrorCodes.AccessDenied =>
                "You do not have permission to access clinic profile settings.",
            ClinicSettingsErrorCodes.InvalidScope =>
                "The selected clinic profile scope is invalid.",
            ClinicSettingsErrorCodes.ClinicScopeRequired =>
                "Select a clinic before loading profile settings.",
            ClinicSettingsErrorCodes.ClinicNotFound =>
                "Clinic was not found.",
            ClinicSettingsErrorCodes.EmptyUpdate =>
                "Provide at least one clinic profile field to update.",
            ClinicSettingsErrorCodes.InvalidTimezone =>
                "Default timezone is invalid. Use a valid IANA identifier.",
            ClinicSettingsErrorCodes.ConcurrencyConflict =>
                "Another change was saved first. Reload the latest profile and try again.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            _ => ex.StatusCode switch
            {
                401 => "Sign in to view clinic profile settings.",
                403 => "You do not have permission to access clinic profile settings.",
                404 => "Clinic was not found.",
                409 => "Another change was saved first. Reload the latest profile and try again.",
                _ => ex.ToUserMessage(),
            },
        };
    }
}
