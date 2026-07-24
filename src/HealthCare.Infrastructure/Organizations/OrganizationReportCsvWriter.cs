using System.Globalization;
using System.Text;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Infrastructure.Organizations;

/// <summary>
/// Safe operational CSV builder. Never writes clinical notes, passwords, tokens, or contact payloads.
/// </summary>
internal static class OrganizationReportCsvWriter
{
    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        AppendRow(sb, headers);
        foreach (var row in rows)
        {
            AppendRow(sb, row);
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(Escape(cells[i]));
        }

        sb.Append('\n');
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(',')
            || value.Contains('"')
            || value.Contains('\n')
            || value.Contains('\r');
        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
