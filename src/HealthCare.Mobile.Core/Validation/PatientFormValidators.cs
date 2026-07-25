using System.Text.RegularExpressions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;

namespace HealthCare.Mobile.Core.Validation;

/// <summary>
/// Client-side validation aligned with Application FluentValidation rules.
/// Backend remains authoritative.
/// </summary>
public static class PatientFormValidators
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Upper = new("[A-Z]", RegexOptions.Compiled);
    private static readonly Regex Lower = new("[a-z]", RegexOptions.Compiled);
    private static readonly Regex Digit = new("[0-9]", RegexOptions.Compiled);
    private static readonly Regex NonAlpha = new("[^a-zA-Z0-9]", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string[]> ValidateSignIn(string email, string password)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        RequireEmail(email, errors);
        if (string.IsNullOrWhiteSpace(password))
        {
            Add(errors, nameof(LoginRequest.Password), "Password is required.");
        }

        return ToResult(errors);
    }

    public static IReadOnlyDictionary<string, string[]> ValidateRegistration(PatientRegisterRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        RequireEmail(request.Email, errors);

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            Add(errors, nameof(request.Password), "Password is required.");
        }
        else
        {
            if (request.Password.Length < 8)
            {
                Add(errors, nameof(request.Password), "Password must be at least 8 characters.");
            }

            if (request.Password.Length > 256)
            {
                Add(errors, nameof(request.Password), "Password must be at most 256 characters.");
            }

            if (!Upper.IsMatch(request.Password))
            {
                Add(errors, nameof(request.Password), "Password must contain an uppercase letter.");
            }

            if (!Lower.IsMatch(request.Password))
            {
                Add(errors, nameof(request.Password), "Password must contain a lowercase letter.");
            }

            if (!Digit.IsMatch(request.Password))
            {
                Add(errors, nameof(request.Password), "Password must contain a digit.");
            }

            if (!NonAlpha.IsMatch(request.Password))
            {
                Add(errors, nameof(request.Password), "Password must contain a non-alphanumeric character.");
            }
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            Add(errors, nameof(request.ConfirmPassword), "Passwords do not match.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            Add(errors, nameof(request.FirstName), "First name is required.");
        }
        else if (request.FirstName.Length > 100)
        {
            Add(errors, nameof(request.FirstName), "First name must be at most 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            Add(errors, nameof(request.LastName), "Last name is required.");
        }
        else if (request.LastName.Length > 100)
        {
            Add(errors, nameof(request.LastName), "Last name must be at most 100 characters.");
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && request.PhoneNumber.Length > 32)
        {
            Add(errors, nameof(request.PhoneNumber), "Phone number must be at most 32 characters.");
        }

        if (request.DateOfBirth is { } dob && dob > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            Add(errors, nameof(request.DateOfBirth), "Date of birth cannot be in the future.");
        }

        return ToResult(errors);
    }

    public static IReadOnlyDictionary<string, string[]> ValidateProfileEdit(PatientProfileEditModel model)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(model.FirstName))
        {
            Add(errors, nameof(model.FirstName), "First name is required.");
        }
        else if (model.FirstName.Length > 100)
        {
            Add(errors, nameof(model.FirstName), "First name must be at most 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(model.LastName))
        {
            Add(errors, nameof(model.LastName), "Last name is required.");
        }
        else if (model.LastName.Length > 100)
        {
            Add(errors, nameof(model.LastName), "Last name must be at most 100 characters.");
        }

        if (model.MiddleName is { Length: > 100 })
        {
            Add(errors, nameof(model.MiddleName), "Middle name must be at most 100 characters.");
        }

        if (model.Gender is { Length: > 32 })
        {
            Add(errors, nameof(model.Gender), "Gender must be at most 32 characters.");
        }

        if (model.MobileNumber is { Length: > 32 })
        {
            Add(errors, nameof(model.MobileNumber), "Mobile number must be at most 32 characters.");
        }

        if (model.PreferredLanguage is { Length: > 16 })
        {
            Add(errors, nameof(model.PreferredLanguage), "Preferred language must be at most 16 characters.");
        }

        if (model.Address is { Length: > 500 })
        {
            Add(errors, nameof(model.Address), "Address must be at most 500 characters.");
        }

        if (model.EmergencyContact is { Length: > 250 })
        {
            Add(errors, nameof(model.EmergencyContact), "Emergency contact must be at most 250 characters.");
        }

        if (model.DateOfBirth is { } dob && dob > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            Add(errors, nameof(model.DateOfBirth), "Date of birth cannot be in the future.");
        }

        return ToResult(errors);
    }

    public static UpdatePatientProfileRequest ToUpdateRequest(PatientProfileEditModel model, int expectedVersion)
    {
        var request = new UpdatePatientProfileRequest { ExpectedVersion = expectedVersion };
        request.FirstName = model.FirstName.Trim();
        request.MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName.Trim();
        request.LastName = model.LastName.Trim();
        request.DateOfBirth = model.DateOfBirth;
        request.Gender = string.IsNullOrWhiteSpace(model.Gender) ? null : model.Gender.Trim();
        request.MobileNumber = string.IsNullOrWhiteSpace(model.MobileNumber) ? null : model.MobileNumber.Trim();
        request.PreferredLanguage = string.IsNullOrWhiteSpace(model.PreferredLanguage) ? null : model.PreferredLanguage.Trim();
        request.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
        request.EmergencyContact = string.IsNullOrWhiteSpace(model.EmergencyContact) ? null : model.EmergencyContact.Trim();
        return request;
    }

    private static void RequireEmail(string email, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            Add(errors, "Email", "Email is required.");
            return;
        }

        if (email.Length > 256 || !EmailRegex.IsMatch(email.Trim()))
        {
            Add(errors, "Email", "Enter a valid email address.");
        }
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = [];
            errors[key] = list;
        }

        list.Add(message);
    }

    private static IReadOnlyDictionary<string, string[]> ToResult(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
}

public sealed class PatientProfileEditModel
{
    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? MobileNumber { get; set; }

    public string? PreferredLanguage { get; set; }

    public string? Address { get; set; }

    public string? EmergencyContact { get; set; }

    public static PatientProfileEditModel FromResponse(PatientProfileResponse profile) =>
        new()
        {
            FirstName = profile.FirstName,
            MiddleName = profile.MiddleName,
            LastName = profile.LastName,
            DateOfBirth = profile.DateOfBirth,
            Gender = profile.Gender,
            MobileNumber = profile.MobileNumber,
            PreferredLanguage = profile.PreferredLanguage,
            Address = profile.Address,
            EmergencyContact = profile.EmergencyContact,
        };
}
