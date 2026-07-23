using System.Net;
using System.Text.Json;

namespace HealthCare.Web.Services;

public sealed class ApiProblemException : Exception
{
    public ApiProblemException(
        int statusCode,
        string title,
        string? detail,
        string? errorCode,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
        : base(detail ?? title)
    {
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        ErrorCode = errorCode;
        ValidationErrors = validationErrors;
    }

    public int StatusCode { get; }

    public string Title { get; }

    public string? Detail { get; }

    public string? ErrorCode { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public static async Task<ApiProblemException> FromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        string? title = response.ReasonPhrase;
        string? detail = null;
        string? errorCode = null;
        Dictionary<string, string[]>? validation = null;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;
            if (root.TryGetProperty("title", out var titleEl))
            {
                title = titleEl.GetString() ?? title;
            }

            if (root.TryGetProperty("detail", out var detailEl))
            {
                detail = detailEl.GetString();
            }

            if (root.TryGetProperty("errorCode", out var codeEl))
            {
                errorCode = codeEl.GetString();
            }
            else if (root.TryGetProperty("extensions", out var ext)
                     && ext.ValueKind == JsonValueKind.Object
                     && ext.TryGetProperty("errorCode", out var extCode))
            {
                errorCode = extCode.GetString();
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                validation = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in errors.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        validation[prop.Name] = prop.Value.EnumerateArray()
                            .Select(x => x.GetString() ?? string.Empty)
                            .Where(x => x.Length > 0)
                            .ToArray();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Keep status-based fallbacks.
        }

        title ??= $"Request failed ({(int)response.StatusCode})";
        return new ApiProblemException((int)response.StatusCode, title, detail, errorCode, validation);
    }

    public string ToUserMessage()
    {
        if (ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ValidationErrors.SelectMany(kv => kv.Value));
        }

        if (!string.IsNullOrWhiteSpace(Detail))
        {
            return Detail!;
        }

        return StatusCode switch
        {
            (int)HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            (int)HttpStatusCode.Forbidden => "You do not have permission to perform this action.",
            (int)HttpStatusCode.NotFound => "The requested resource was not found.",
            (int)HttpStatusCode.Conflict => Title,
            >= 500 => "A server error occurred. Please try again later.",
            _ => Title,
        };
    }
}
