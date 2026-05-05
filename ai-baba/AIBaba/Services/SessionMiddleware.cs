namespace AIBaba.Services;

/// <summary>
/// Tiny middleware that gives every visitor a stable sessionId via the
/// 'aibaba_sid' cookie. Used as the key into IMemoryStore.
/// </summary>
public class SessionMiddleware
{
    public const string CookieName = "aibaba_sid";
    private readonly RequestDelegate _next;

    public SessionMiddleware(RequestDelegate next) { _next = next; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Cookies.TryGetValue(CookieName, out var sid) || string.IsNullOrEmpty(sid))
        {
            sid = Guid.NewGuid().ToString("N");
            ctx.Response.Cookies.Append(CookieName, sid, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
        }
        ctx.Items["sid"] = sid;
        await _next(ctx);
    }

    public static string GetSid(HttpContext ctx) =>
        (ctx.Items["sid"] as string) ?? "anonymous";
}
