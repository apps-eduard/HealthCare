namespace HealthCare.Web.Auth;

/// <summary>
/// Validates local-only return URLs to prevent open redirects.
/// </summary>
public static class SafeReturnUrl
{
    public const string DefaultPath = "/dashboard";

    /// <summary>
    /// Returns a safe local path. Invalid, absolute, or protocol-relative values fall back to <see cref="DefaultPath"/>.
    /// </summary>
    public static string Resolve(string? returnUrl)
    {
        if (TryValidate(returnUrl, out var safe))
        {
            return safe;
        }

        return DefaultPath;
    }

    /// <summary>
    /// Builds <c>/login?returnUrl=...</c> for a local path (or default login when the path is empty/login).
    /// </summary>
    public static string BuildLoginUrl(string? requestedPath)
    {
        if (!TryValidate(requestedPath, out var path) || IsLoginPath(path))
        {
            return "/login";
        }

        return $"/login?returnUrl={Uri.EscapeDataString(path)}";
    }

    public static bool TryValidate(string? returnUrl, out string safePath)
    {
        safePath = DefaultPath;
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return false;
        }

        var candidate = returnUrl.Trim();
        try
        {
            candidate = Uri.UnescapeDataString(candidate);
        }
        catch (UriFormatException)
        {
            return false;
        }

        // Reject absolute and protocol-relative URLs.
        if (candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.Contains("://", StringComparison.OrdinalIgnoreCase)
            || candidate.Contains('\\'))
        {
            return false;
        }

        if (!candidate.StartsWith('/'))
        {
            return false;
        }

        // Reject encoded tricks and control characters.
        if (candidate.Any(char.IsControl))
        {
            return false;
        }

        // Normalize: path only (keep query/fragment if local).
        if (!Uri.TryCreate(candidate, UriKind.Relative, out _))
        {
            return false;
        }

        // Extra guard: no host-looking segment after scheme-smuggling.
        if (candidate.StartsWith("/\\", StringComparison.Ordinal)
            || candidate.Contains('@'))
        {
            return false;
        }

        if (IsLoginPath(candidate))
        {
            return false;
        }

        safePath = candidate;
        return true;
    }

    public static string FromNavigationUri(string absoluteUri, string baseUri)
    {
        if (!Uri.TryCreate(absoluteUri, UriKind.Absolute, out var uri)
            || !Uri.TryCreate(baseUri, UriKind.Absolute, out var bas))
        {
            return DefaultPath;
        }

        if (!string.Equals(uri.Scheme, bas.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Authority, bas.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultPath;
        }

        var relative = uri.PathAndQuery;
        return TryValidate(relative, out var safe) ? safe : DefaultPath;
    }

    private static bool IsLoginPath(string path) =>
        path.Equals("/login", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/login?", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/login#", StringComparison.OrdinalIgnoreCase);
}
