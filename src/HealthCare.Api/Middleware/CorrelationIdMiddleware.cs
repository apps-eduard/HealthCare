using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace HealthCare.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.TraceIdentifier = correlationId;
        context.Items[ItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            Activity.Current?.SetTag("correlation.id", correlationId);
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues values)
            && !StringValues.IsNullOrEmpty(values))
        {
            var provided = values.ToString();
            if (!string.IsNullOrWhiteSpace(provided))
            {
                return provided.Trim();
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
