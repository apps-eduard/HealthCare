using HealthCare.Contracts.Clinics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize]
[Route("api/v1/reference")]
public sealed class ReferenceController : ControllerBase
{
    [HttpGet("timezones")]
    [ProducesResponseType(typeof(IReadOnlyList<TimeZoneInfoResponse>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<TimeZoneInfoResponse>> GetTimeZones()
    {
        var now = DateTimeOffset.UtcNow;
        var items = TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(tz => tz.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(tz =>
            {
                var offset = tz.GetUtcOffset(now);
                var sign = offset < TimeSpan.Zero ? "-" : "+";
                var formatted = $"{sign}{offset:hh\\:mm}";
                return new TimeZoneInfoResponse
                {
                    TimeZoneId = tz.Id,
                    DisplayName = tz.DisplayName,
                    UtcOffset = $"UTC{formatted}",
                };
            })
            .ToList();

        // Ensure Asia/Riyadh is present for hosts that only expose Windows IDs.
        if (!items.Any(i => string.Equals(i.TimeZoneId, "Asia/Riyadh", StringComparison.OrdinalIgnoreCase)))
        {
            items.Insert(0, new TimeZoneInfoResponse
            {
                TimeZoneId = "Asia/Riyadh",
                DisplayName = "(UTC+03:00) Riyadh",
                UtcOffset = "UTC+03:00",
            });
        }

        return Ok(items);
    }
}
