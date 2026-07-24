using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.Availability;

public static class AvailabilityPresentation
{
    public const int MinSlotDurationMinutes = 10;
    public const int MaxSlotDurationMinutes = 240;

    /// <summary>Monday-first ordering for clinic scheduling UI.</summary>
    public static readonly IReadOnlyList<string> DaysOfWeekOrdered =
    [
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
    ];

    public static readonly IReadOnlyList<string> ExceptionTypes =
    [
        "UnavailableFullDay",
        "UnavailableRange",
        "CustomAvailableRange",
    ];

    public static string DayLabel(string? day) =>
        string.IsNullOrWhiteSpace(day) ? "Unknown" : day.Trim();

    public static string ExceptionTypeLabel(string? type) =>
        type switch
        {
            "UnavailableFullDay" => "Unavailable (full day)",
            "UnavailableRange" => "Unavailable (time range)",
            "CustomAvailableRange" => "Custom available range",
            _ => string.IsNullOrWhiteSpace(type) ? "Unknown" : type.Trim(),
        };

    public static StatusTone ActiveTone(bool isActive) =>
        isActive ? StatusTone.Success : StatusTone.Default;

    public static string ActiveLabel(bool isActive) =>
        isActive ? "Active" : "Inactive";

    public static string FormatEffectiveRange(DateOnly from, DateOnly? to) =>
        to is DateOnly end ? $"{from:yyyy-MM-dd} → {end:yyyy-MM-dd}" : $"{from:yyyy-MM-dd} → open";

    public static string FormatLocalWindow(string start, string end) =>
        $"{start} – {end}";

    public static bool IsValidDuration(int minutes) =>
        minutes is >= MinSlotDurationMinutes and <= MaxSlotDurationMinutes;

    public static bool TryParseLocalTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParse(value, out time);

    public static bool IsValidWindow(string? startLocal, string? endLocal) =>
        TryParseLocalTime(startLocal, out var start)
        && TryParseLocalTime(endLocal, out var end)
        && start < end;

    public static bool ExceptionRequiresTimes(string? exceptionType) =>
        !string.Equals(exceptionType, "UnavailableFullDay", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, IReadOnlyList<Contracts.Appointments.DoctorAvailabilityResponse>> GroupByDay(
        IEnumerable<Contracts.Appointments.DoctorAvailabilityResponse> windows)
    {
        var lookup = windows
            .GroupBy(w => DayLabel(w.DayOfWeek), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Contracts.Appointments.DoctorAvailabilityResponse>)g.OrderBy(x => x.StartLocalTime).ToList(), StringComparer.OrdinalIgnoreCase);

        var ordered = new Dictionary<string, IReadOnlyList<Contracts.Appointments.DoctorAvailabilityResponse>>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in DaysOfWeekOrdered)
        {
            if (lookup.TryGetValue(day, out var list) && list.Count > 0)
            {
                ordered[day] = list;
            }
        }

        return ordered;
    }
}

/// <summary>
/// Client-side effective availability composition for Org Admin review (no separate API).
/// </summary>
public static class AvailabilityEffectiveComposer
{
    public sealed record DaySummary(
        DateOnly Date,
        string DayOfWeek,
        IReadOnlyList<WindowSlice> Windows,
        IReadOnlyList<Contracts.Appointments.DoctorAvailabilityExceptionResponse> Exceptions);

    public sealed record WindowSlice(
        string StartLocalTime,
        string EndLocalTime,
        int SlotDurationMinutes,
        bool IsActive);

    public static IReadOnlyList<DaySummary> Build(
        IEnumerable<Contracts.Appointments.DoctorAvailabilityResponse> windows,
        IEnumerable<Contracts.Appointments.DoctorAvailabilityExceptionResponse> exceptions,
        DateOnly from,
        DateOnly to)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Cap UI composition to two weeks to keep the tab readable.
        if (to.DayNumber - from.DayNumber > 13)
        {
            to = from.AddDays(13);
        }

        var windowList = windows.ToList();
        var exceptionList = exceptions.ToList();
        var days = new List<DaySummary>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayName = date.DayOfWeek.ToString();
            var applicable = windowList
                .Where(w => string.Equals(w.DayOfWeek, dayName, StringComparison.OrdinalIgnoreCase)
                            && w.EffectiveFrom <= date
                            && (w.EffectiveTo is null || w.EffectiveTo >= date))
                .OrderBy(w => w.StartLocalTime)
                .Select(w => new WindowSlice(w.StartLocalTime, w.EndLocalTime, w.SlotDurationMinutes, w.IsActive))
                .ToList();

            var dayExceptions = exceptionList
                .Where(e => e.Date == date)
                .OrderBy(e => e.StartLocalTime)
                .ToList();

            days.Add(new DaySummary(date, dayName, applicable, dayExceptions));
        }

        return days;
    }
}

public static class AvailabilityPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.AvailabilityRead) || CanManage(permissions);

    public static bool CanManage(IPermissionState permissions) =>
        permissions.Has(WebPermissions.AvailabilityManageSelf)
        || permissions.Has(WebPermissions.AvailabilityManageClinic)
        || permissions.Has(WebPermissions.AvailabilityManageOrganization);

    public static bool IsSelfOnly(IPermissionState permissions) =>
        permissions.Has(WebPermissions.AvailabilityManageSelf)
        && !permissions.Has(WebPermissions.AvailabilityManageClinic)
        && !permissions.Has(WebPermissions.AvailabilityManageOrganization);
}

public static class AvailabilityProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "appointment.outside_availability" =>
                "The selected time is outside doctor availability.",
            "appointment.availability_exception" =>
                "The selected time is blocked by an availability exception.",
            "appointment.invalid_slot_duration" =>
                "Slot duration must be between 10 and 240 minutes.",
            "appointment.availability_conflict" =>
                "This availability window overlaps an existing window.",
            "appointment.availability_concurrency_conflict" =>
                "This availability record was updated by someone else. Reload and try again.",
            "appointment.invalid_availability" =>
                "The availability definition is invalid.",
            "appointment.doctor_not_found" =>
                "Doctor was not found or you do not have access.",
            "appointment.slot_unavailable" =>
                "That time slot is no longer available.",
            "appointment.invalid_timezone" =>
                "The clinic timezone is invalid.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            "authz.clinic_access_denied" =>
                "That clinic is outside your organization scope.",
            _ => ex.ToUserMessage(),
        };
    }

    public static bool IsConcurrencyConflict(ApiProblemException ex) =>
        string.Equals(ex.ErrorCode, "appointment.availability_concurrency_conflict", StringComparison.Ordinal)
        || (ex.StatusCode == 409
            && (ex.Detail?.Contains("version", StringComparison.OrdinalIgnoreCase) == true
                || ex.Title?.Contains("concurrency", StringComparison.OrdinalIgnoreCase) == true
                || ex.Title?.Contains("modified", StringComparison.OrdinalIgnoreCase) == true));
}
