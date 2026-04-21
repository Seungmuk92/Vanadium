using Serilog.Context;
using System.Diagnostics;

namespace Vanadium.Note.REST.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        var level = context.Response.StatusCode >= 500 ? LogLevel.Error
                  : context.Response.StatusCode >= 400 ? LogLevel.Warning
                  : LogLevel.Information;

        // HttpContext.User is populated after authentication middleware runs
        var username = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name
            : null;

        using (username is not null ? LogContext.PushProperty("Username", username) : null)
        {
            logger.Log(level,
                "{Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds);
        }
    }
}
