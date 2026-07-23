using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Configuration;

public sealed class HangfireOptionsValidator : IValidateOptions<HangfireOptions>
{
    public ValidateOptionsResult Validate(string? name, HangfireOptions options)
    {
        var errors = new List<string>();

        if (options.Enabled)
        {
            if (options.WorkerCount < HangfireOptions.MinWorkerCount
                || options.WorkerCount > HangfireOptions.MaxWorkerCount)
            {
                errors.Add(
                    $"Hangfire:WorkerCount must be between {HangfireOptions.MinWorkerCount} and {HangfireOptions.MaxWorkerCount} when Hangfire is enabled.");
            }

            var queues = NormalizeQueues(options.Queues, out var queueErrors);
            errors.AddRange(queueErrors);
            if (queues.Count == 0)
            {
                errors.Add("Hangfire:Queues must contain at least one queue when Hangfire is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.ServerName)
                || options.ServerName.Trim().Length is < 1 or > 100)
            {
                errors.Add("Hangfire:ServerName must be 1–100 characters when Hangfire is enabled.");
            }

            if (options.ShutdownTimeoutSeconds < HangfireOptions.MinShutdownTimeoutSeconds
                || options.ShutdownTimeoutSeconds > HangfireOptions.MaxShutdownTimeoutSeconds)
            {
                errors.Add(
                    $"Hangfire:ShutdownTimeoutSeconds must be between {HangfireOptions.MinShutdownTimeoutSeconds} and {HangfireOptions.MaxShutdownTimeoutSeconds}.");
            }
        }

        if (options.Dashboard.Enabled)
        {
            var path = options.Dashboard.Path?.Trim() ?? string.Empty;
            if (!path.StartsWith('/') || path.Length < 2 || path.Contains(' ', StringComparison.Ordinal))
            {
                errors.Add("Hangfire:Dashboard:Path must start with '/' and contain no spaces when the dashboard is enabled.");
            }

            if (path is "/" or "//")
            {
                errors.Add("Hangfire:Dashboard:Path must not be the site root.");
            }
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    public static List<string> NormalizeQueues(string[]? queues, out List<string> errors)
    {
        errors = [];
        var result = new List<string>();
        if (queues is null)
        {
            return result;
        }

        foreach (var raw in queues)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                errors.Add("Hangfire queue names must not be empty.");
                continue;
            }

            var name = raw.Trim().ToLowerInvariant();
            if (!IsValidQueueName(name))
            {
                errors.Add($"Hangfire queue name '{raw}' is invalid. Use lowercase letters, digits, hyphen, or underscore.");
                continue;
            }

            if (!result.Contains(name, StringComparer.Ordinal))
            {
                result.Add(name);
            }
        }

        return result;
    }

    public static bool IsValidQueueName(string name) =>
        name.Length is >= 1 and <= 50
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
