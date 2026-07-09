namespace Vanadium.Note.REST.Middleware;

/// <summary>
/// Adds baseline security response headers. Currently emits
/// <c>X-Content-Type-Options: nosniff</c> to stop browsers from MIME-sniffing
/// responses away from their declared Content-Type. Broader headers (CSP, etc.)
/// are intentionally left for a separate change (see issue #113 scope).
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        await next(context);
    }
}
