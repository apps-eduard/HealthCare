using HealthCare.Contracts.Patients;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.Patients;

/// <summary>
/// Centralized patient / clinic-patient status presentation. Uses exact API values.
/// </summary>
public static class PatientStatusPresentation
{
    public static readonly IReadOnlyList<string> ClinicPatientStatuses = ["Active", "Inactive"];

    public static string ClinicPatientLabel(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim();

    public static StatusTone ClinicPatientTone(string? status) =>
        status switch
        {
            "Active" => StatusTone.Success,
            "Inactive" => StatusTone.Default,
            _ => StatusTone.Warning,
        };

    public static string PatientActiveLabel(bool isActive) =>
        isActive ? "Active" : "Inactive";

    public static StatusTone PatientActiveTone(bool isActive) =>
        isActive ? StatusTone.Success : StatusTone.Default;
}

/// <summary>
/// Shared safe display helpers for directory and PatientPicker.
/// </summary>
public static class PatientDisplay
{
    public static string FullName(StaffPatientSummaryResponse p)
    {
        var parts = new[] { p.FirstName, p.MiddleName, p.LastName }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var name = string.Join(' ', parts).Trim();
        return string.IsNullOrWhiteSpace(name) ? "—" : name;
    }

    public static string PickerLabel(StaffPatientSummaryResponse p) =>
        $"{FullName(p)} · {p.LocalPatientNumber} · {PatientStatusPresentation.ClinicPatientLabel(p.ClinicPatientStatus)}";

    /// <summary>
    /// Masks middle digits for dense list views while keeping enough for recognition.
    /// </summary>
    public static string MaskMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return "—";
        }

        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        if (digits.Length < 6)
        {
            return "••••";
        }

        var last4 = digits[^4..];
        return $"•••-•••-{last4}";
    }
}

public static class PatientProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "patient.not_found" or "patient.not_found_or_denied" or "patient.access_denied" =>
                "Patient was not found or you do not have access.",
            "patient.concurrency_conflict" or "patient.clinic_patient_concurrency_conflict" =>
                "This clinic patient record was updated by someone else. Reload and try again.",
            "patient.invalid_search" =>
                "The search filters are not valid. Adjust them and try again.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            _ => ex.ToUserMessage(),
        };
    }

    public static bool IsConcurrencyConflict(ApiProblemException ex) =>
        string.Equals(ex.ErrorCode, "patient.concurrency_conflict", StringComparison.Ordinal)
        || string.Equals(ex.ErrorCode, "patient.clinic_patient_concurrency_conflict", StringComparison.Ordinal)
        || (ex.StatusCode == 409
            && (ex.Detail?.Contains("version", StringComparison.OrdinalIgnoreCase) == true
                || ex.Title?.Contains("concurrency", StringComparison.OrdinalIgnoreCase) == true));

    public static bool IsNotFound(ApiProblemException ex) =>
        ex.StatusCode == 404
        || string.Equals(ex.ErrorCode, "patient.not_found", StringComparison.Ordinal)
        || string.Equals(ex.ErrorCode, "patient.not_found_or_denied", StringComparison.Ordinal);
}
