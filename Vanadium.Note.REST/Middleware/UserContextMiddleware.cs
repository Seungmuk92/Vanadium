using Serilog.Context;

namespace Vanadium.Note.REST.Middleware;

public class UserContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var username = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name
            : null;

        if (username is null)
        {
            await next(context);
            return;
        }

        using (LogContext.PushProperty("Username", username))
            await next(context);
    }
}
