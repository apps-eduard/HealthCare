namespace HealthCare.Web.Configuration;

public sealed class BffOptions
{
    public const string SectionName = "Bff";

    /// <summary>Idle timeout for the server token session (sliding).</summary>
    public int SessionIdleMinutes { get; set; } = 30;

    /// <summary>Absolute lifetime for the server token session and auth cookie.</summary>
    public int AbsoluteSessionHours { get; set; } = 8;

    /// <summary>
    /// Token store backing. MVP uses DistributedCache (memory in Development).
    /// Production should configure a shared distributed cache (e.g. Redis).
    /// </summary>
    public string TokenStore { get; set; } = "DistributedCache";

    /// <summary>
    /// Auth cookie name. Prefer __Host-HealthCare.Staff when HTTPS is required.
    /// Development over HTTP should use HealthCare.Staff.Auth (__Host- requires Secure).
    /// </summary>
    public string CookieName { get; set; } = "HealthCare.Staff.Auth";

    public bool RequireHttps { get; set; } = true;
}
