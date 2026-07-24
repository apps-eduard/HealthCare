using System.Text;
using System.Text.RegularExpressions;

namespace HealthCare.Web.Clinics;

/// <summary>
/// Client-side slug helpers only. Backend remains authoritative for uniqueness and format.
/// </summary>
public static partial class ClinicSlugHelper
{
    public static string SuggestFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var normalized = name.Trim().ToLowerInvariant();
        var sb = new StringBuilder(normalized.Length);
        var lastHyphen = false;
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastHyphen = false;
            }
            else if ((ch is ' ' or '-' or '_') && !lastHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastHyphen = true;
            }
        }

        var result = sb.ToString().Trim('-');
        return result.Length > 80 ? result[..80].TrimEnd('-') : result;
    }

    public static bool LooksValid(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        return SlugPattern().IsMatch(slug.Trim());
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
