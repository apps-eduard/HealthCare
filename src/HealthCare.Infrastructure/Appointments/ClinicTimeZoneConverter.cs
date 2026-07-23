using HealthCare.Application.Appointments;
using HealthCare.Domain.Appointments;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class ClinicTimeZoneConverter : IClinicTimeZoneConverter
{
    private readonly ILogger<ClinicTimeZoneConverter> _logger;

    public ClinicTimeZoneConverter(ILogger<ClinicTimeZoneConverter> logger)
    {
        _logger = logger;
    }

    public TimeZoneInfo Resolve(string timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId)
            ? AvailabilitySlotRules.DefaultTimeZoneId
            : timeZoneId.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows hosts without IANA data may only know "Arab Standard Time" for Riyadh.
            if (string.Equals(id, AvailabilitySlotRules.DefaultTimeZoneId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Asia/Riyadh", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
                }
                catch (TimeZoneNotFoundException)
                {
                    // fall through
                }
            }

            _logger.LogWarning("Unknown timezone {TimeZoneId}; falling back to {Fallback}", id, AvailabilitySlotRules.DefaultTimeZoneId);
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(AvailabilitySlotRules.DefaultTimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
                }
                catch (TimeZoneNotFoundException)
                {
                    throw AvailabilityException.InvalidTimeZone();
                }
            }
        }
        catch (InvalidTimeZoneException)
        {
            throw AvailabilityException.InvalidTimeZone();
        }
    }

    public DateTimeOffset ToUtc(DateOnly date, TimeOnly localTime, string timeZoneId)
    {
        var tz = Resolve(timeZoneId);
        var local = date.ToDateTime(localTime, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public DateTimeOffset ToClinicLocal(DateTimeOffset utc, string timeZoneId)
    {
        var tz = Resolve(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(utc.UtcDateTime, tz);
        var offset = tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public DateOnly GetClinicDate(DateTimeOffset utc, string timeZoneId)
    {
        var local = ToClinicLocal(utc, timeZoneId);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public TimeOnly GetClinicTime(DateTimeOffset utc, string timeZoneId)
    {
        var local = ToClinicLocal(utc, timeZoneId);
        return TimeOnly.FromDateTime(local.DateTime);
    }
}
