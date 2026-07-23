using MudBlazor;

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

    public static Color ChipColor(string? status) =>
        status switch
        {
            "Requested" => Color.Warning,
            "Confirmed" => Color.Info,
            "CheckedIn" => Color.Primary,
            "InProgress" => Color.Secondary,
            "Completed" => Color.Success,
            "NoShow" => Color.Dark,
            "CancelledByPatient" => Color.Default,
            "CancelledByClinic" => Color.Default,
            _ => Color.Default,
        };

    public static bool IsTerminal(string? status) =>
        status is "Completed" or "NoShow" or "CancelledByPatient" or "CancelledByClinic";
}
