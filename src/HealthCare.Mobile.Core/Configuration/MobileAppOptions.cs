namespace HealthCare.Mobile.Core.Configuration;

/// <summary>
/// Patient mobile environment options. Bound from appsettings / embedded JSON.
/// Do not place secrets here.
/// </summary>
public sealed class MobileAppOptions
{
    public const string SectionName = "Mobile";

    /// <summary>Logical environment name (Development, Emulator, Device, Staging, Production).</summary>
    public string EnvironmentName { get; set; } = "Development";

    /// <summary>
    /// Absolute API base URL including scheme and port, without trailing slash.
    /// Examples: https://10.0.2.2:7081 (Android emulator → host HTTPS),
    /// http://10.0.2.2:5080 (emulator cleartext Dev only).
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://10.0.2.2:5080";

    /// <summary>HTTP timeout for API calls.</summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, Android may use cleartext HTTP (Development / emulator only).
    /// Must never be enabled for Production builds.
    /// </summary>
    public bool AllowCleartextHttp { get; set; } = true;
}

public static class MobileAppOptionsValidator
{
    public static IReadOnlyList<string> Validate(MobileAppOptions options)
    {
        var errors = new List<string>();

        if (options is null)
        {
            errors.Add("Mobile options are required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(options.EnvironmentName))
        {
            errors.Add("EnvironmentName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            errors.Add("ApiBaseUrl is required.");
        }
        else if (!Uri.TryCreate(options.ApiBaseUrl.TrimEnd('/'), UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("ApiBaseUrl must be an absolute http or https URL.");
        }
        else if (uri.Scheme == Uri.UriSchemeHttp
                 && !options.AllowCleartextHttp
                 && !IsLoopback(uri))
        {
            errors.Add("Http ApiBaseUrl requires AllowCleartextHttp=true outside loopback.");
        }

        if (options.HttpTimeoutSeconds is < 5 or > 120)
        {
            errors.Add("HttpTimeoutSeconds must be between 5 and 120.");
        }

        if (string.Equals(options.EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase)
            && options.AllowCleartextHttp)
        {
            errors.Add("AllowCleartextHttp must be false in Production.");
        }

        return errors;
    }

    public static Uri GetNormalizedBaseAddress(MobileAppOptions options)
    {
        var errors = Validate(options);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var text = options.ApiBaseUrl.Trim().TrimEnd('/') + "/";
        return new Uri(text, UriKind.Absolute);
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback
        || string.Equals(uri.Host, "10.0.2.2", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}
