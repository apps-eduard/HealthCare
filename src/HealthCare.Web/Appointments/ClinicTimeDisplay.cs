namespace HealthCare.Web.Appointments;

/// <summary>
/// Converts API UTC timestamps to a clinic timezone for display. Never uses browser-local time silently.
/// </summary>
public static class ClinicTimeDisplay
{
    public static DateTimeOffset ToClinicLocal(DateTimeOffset utc, string? timeZoneId)
    {
        var tz = ResolveTimeZone(timeZoneId);
        return TimeZoneInfo.ConvertTime(utc, tz);
    }

    public static string FormatLocal(DateTimeOffset utc, string? timeZoneId, string format = "yyyy-MM-dd HH:mm")
    {
        var local = ToClinicLocal(utc, timeZoneId);
        return local.ToString(format);
    }

    public static string FormatLocalWithZone(DateTimeOffset utc, string? timeZoneId, string format = "yyyy-MM-dd HH:mm")
    {
        var label = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        return $"{FormatLocal(utc, timeZoneId, format)} ({label})";
    }

    public static DateOnly ToClinicDate(DateTimeOffset utc, string? timeZoneId)
    {
        var local = ToClinicLocal(utc, timeZoneId);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        var id = timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Windows hosts often lack IANA ids; mirror API converter for common Gulf TZ.
            if (string.Equals(id, "Asia/Riyadh", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }

            return TimeZoneInfo.Utc;
        }
    }
}
