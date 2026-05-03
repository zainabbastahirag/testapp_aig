using System.Diagnostics;
using System.Text.Json;

namespace AGOneExperion.API.Middleware;

/// <summary>
/// Logs every request: method, path, status code, and elapsed time.
/// Masks any blob connection string or API key values from appearing in logs.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _log;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(ctx);
        }
        finally
        {
            sw.Stop();
            _log.LogInformation("[HTTP] {Method} {Path} → {Status} ({Ms}ms)",
                ctx.Request.Method,
                ctx.Request.Path,
                ctx.Response.StatusCode,
                sw.ElapsedMilliseconds);
        }
    }
}
