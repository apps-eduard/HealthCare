using HealthCare.Web.Design;

namespace HealthCare.Web.Appointments;

/// <summary>
/// Centralized appointment status display. Uses exact API status strings.
/// </summary>
public static class AppointmentStatusPresentation
{
    public static readonly IReadOnlyList<string> AllStatuses =
    [
        "Requested",
        "Confirmed",
        "CheckedIn",
        "InProgress",
        "Completed",
        "NoShow",
        "CancelledByPatient",
        "CancelledByClinic",
    ];

    public static string DisplayLabel(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim();

    public static StatusTone ChipTone(string? status) =>
        status switch
        {
            "Requested" => StatusTone.Warning,
            "Confirmed" => StatusTone.Info,
            "CheckedIn" => StatusTone.Primary,
            "InProgress" => StatusTone.Neutral,
            "Completed" => StatusTone.Success,
            "NoShow" => StatusTone.Neutral,
            "CancelledByPatient" => StatusTone.Default,
            "CancelledByClinic" => StatusTone.Default,
            _ => StatusTone.Default,
        };

    public static bool IsTerminal(string? status) =>
        status is "Completed" or "NoShow" or "CancelledByPatient" or "CancelledByClinic";
}
