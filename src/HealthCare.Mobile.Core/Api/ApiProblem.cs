using System.Net;
using System.Text.Json;

namespace HealthCare.Mobile.Core.Api;

public enum ApiErrorKind
{
    None,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Validation,
    Network,
    Timeout,
    Server,
    Unknown,
}

public sealed class ApiProblem
{
    public ApiErrorKind Kind { get; init; }

    public int? StatusCode { get; init; }

    public string Title { get; init; } = "Request failed";

    public string? Detail { get; init; }

    public string? ErrorCode { get; init; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }

    public string UserMessage => Kind switch
    {
        ApiErrorKind.Unauthorized => "Your session has expired. Please sign in again.",
        ApiErrorKind.Forbidden => "You do not have permission to perform this action.",
        ApiErrorKind.NotFound => "The requested item is not available.",
        ApiErrorKind.Conflict => Detail ?? "This action conflicts with the current state. Refresh and try again.",
        ApiErrorKind.Validation => Detail ?? "Please correct the highlighted fields.",
        ApiErrorKind.Network => "Unable to reach the server. Check your connection and try again.",
        ApiErrorKind.Timeout => "The server took too long to respond. Please try again.",
        ApiErrorKind.Server => "Something went wrong on the server. Please try again later.",
        _ => "Something went wrong. Please try again.",
    };
}

public sealed class ApiResult<T>
{
    public bool IsSuccess { get; init; }

    public T? Value { get; init; }

    public ApiProblem? Error { get; init; }

    public static ApiResult<T> Success(T value) => new() { IsSuccess = true, Value = value };

    public static ApiResult<T> Failure(ApiProblem error) => new() { IsSuccess = false, Error = error };
}

public static class ApiProblemMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ApiProblem FromStatusCode(HttpStatusCode statusCode, string? body = null)
    {
        var status = (int)statusCode;
        var kind = status switch
        {
            401 => ApiErrorKind.Unauthorized,
            403 => ApiErrorKind.Forbidden,
            404 => ApiErrorKind.NotFound,
            409 => ApiErrorKind.Conflict,
            400 or 422 => ApiErrorKind.Validation,
            >= 500 => ApiErrorKind.Server,
            _ => ApiErrorKind.Unknown,
        };

        string? title = null;
        string? detail = null;
        string? errorCode = null;
        IReadOnlyDictionary<string, string[]>? validation = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("title", out var t))
                {
                    title = t.GetString();
                }

                if (root.TryGetProperty("detail", out var d))
                {
                    detail = SanitizeDetail(d.GetString());
                }

                if (root.TryGetProperty("errorCode", out var c))
                {
                    errorCode = c.GetString();
                }

                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in errors.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            map[prop.Name] = prop.Value.EnumerateArray()
                                .Select(x => x.GetString() ?? string.Empty)
                                .Where(x => x.Length > 0)
                                .ToArray();
                        }
                    }

                    if (map.Count > 0)
                    {
                        validation = map;
                        kind = ApiErrorKind.Validation;
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore unparseable bodies; never surface raw payloads.
            }
        }

        return new ApiProblem
        {
            Kind = kind,
            StatusCode = status,
            Title = title ?? kind.ToString(),
            Detail = detail,
            ErrorCode = errorCode,
            ValidationErrors = validation,
        };
    }

    public static ApiProblem FromException(Exception exception) =>
        exception switch
        {
            TaskCanceledException or OperationCanceledException or TimeoutException => new ApiProblem
            {
                Kind = ApiErrorKind.Timeout,
                Title = "Timeout",
            },
            HttpRequestException => new ApiProblem
            {
                Kind = ApiErrorKind.Network,
                Title = "Network error",
            },
            _ => new ApiProblem
            {
                Kind = ApiErrorKind.Unknown,
                Title = "Unexpected error",
            },
        };

    private static string? SanitizeDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        // Never pass through long internal dumps.
        detail = detail.Trim();
        return detail.Length > 280 ? detail[..280] : detail;
    }
}
