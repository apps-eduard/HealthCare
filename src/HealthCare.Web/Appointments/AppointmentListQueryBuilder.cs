using HealthCare.Contracts.Appointments;

namespace HealthCare.Web.Appointments;

/// <summary>
/// Builds server-side appointment list queries for queue/calendar pages (testable without bUnit).
/// </summary>
public static class AppointmentListQueryBuilder
{
    public static AppointmentListQuery Build(
        DateTime? fromLocalDate,
        DateTime? toLocalDate,
        string? status,
        Guid? doctorStaffMemberId,
        Guid? clinicId,
        int page,
        int pageSize,
        string sortBy,
        string sortDirection,
        string? clinicTimeZoneId = null)
    {
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;

        if (fromLocalDate is DateTime from)
        {
            fromUtc = LocalDateStartToUtc(DateOnly.FromDateTime(from), clinicTimeZoneId);
        }

        if (toLocalDate is DateTime to)
        {
            toUtc = LocalDateEndToUtc(DateOnly.FromDateTime(to), clinicTimeZoneId);
        }

        return new AppointmentListQuery
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            DoctorStaffMemberId = doctorStaffMemberId is Guid d && d != Guid.Empty ? d : null,
            ClinicId = clinicId is Guid c && c != Guid.Empty ? c : null,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100),
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "appointmentDateUtc" : sortBy.Trim(),
            SortDirection = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc",
        };
    }

    public static DateTimeOffset LocalDateStartToUtc(DateOnly date, string? timeZoneId)
    {
        var tz = ClinicTimeDisplay.ResolveTimeZone(timeZoneId);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static DateTimeOffset LocalDateEndToUtc(DateOnly date, string? timeZoneId)
    {
        var tz = ClinicTimeDisplay.ResolveTimeZone(timeZoneId);
        var local = date.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
