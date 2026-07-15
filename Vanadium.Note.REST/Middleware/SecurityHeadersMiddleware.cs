namespace Vanadium.Note.REST.Middleware;

/// <summary>
/// Adds baseline security response headers:
/// <list type="bullet">
///   <item><c>X-Content-Type-Options: nosniff</c> stops browsers from
///   MIME-sniffing responses away from their declared Content-Type.</item>
///   <item>A locked-down <c>Content-Security-Policy</c> (issue #199). This API
///   only ever returns JSON / <c>application/problem+json</c>, never an HTML
///   document meant to run scripts, so it advertises the tightest possible
///   policy: any HTML that somehow leaves an API path cannot load a script,
///   frame content, be framed, or set a base URI. This is a defense-in-depth
///   second line behind the HTML sanitizer — the interactive render surface
///   (the Blazor WASM app and anonymous share page) carries its own,
///   necessarily looser, CSP at the nginx layer.</item>
/// </list>
/// Swagger UI is a legitimate HTML+script surface (Development only), so the CSP
/// is skipped for its paths — a request that reaches this middleware under
/// <c>/swagger</c> must keep loading its inline bundle.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // frame-ancestors and base-uri do NOT fall back to default-src, so they
        // are stated explicitly alongside the 'none' default.
        if (!context.Request.Path.StartsWithSegments("/swagger"))
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

        await next(context);
    }
}
