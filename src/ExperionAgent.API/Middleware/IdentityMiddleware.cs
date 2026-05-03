namespace ExperionAgent.API.Middleware;

/// <summary>
/// Extracts the caller's IP and stores it in HttpContext.Items
/// so controllers can pass it to the identity resolver.
/// </summary>
public class IdentityMiddleware
{
    private readonly RequestDelegate _next;

    public IdentityMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();

        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
            ip = forwarded.Split(',')[0].Trim();

        ctx.Items["ClientIp"] = ip;
        await _next(ctx);
    }
}
