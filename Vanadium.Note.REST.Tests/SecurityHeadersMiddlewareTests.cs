using Microsoft.AspNetCore.Http;
using Vanadium.Note.REST.Middleware;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Unit coverage for <see cref="SecurityHeadersMiddleware"/> (issue #113).
/// The ForwardedHeaders / HSTS wiring in Program.cs relies on framework
/// built-ins configured in the middleware pipeline and cannot be exercised
/// here without an integration host (no WebApplicationFactory harness exists,
/// and adding one is out of scope); it is verified manually instead.
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SetsNosniffHeader()
    {
        var context = new DefaultHttpContext();
        var called = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact]
    public async Task InvokeAsync_SetsLockedDownCspOnApiPaths()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/notes";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
        // The locked-down API policy must never permit script execution.
        Assert.DoesNotContain("script-src", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
    }

    [Fact]
    public async Task InvokeAsync_SkipsCspForSwaggerUi()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/swagger/index.html";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        // Swagger UI serves an inline bundle; a 'none' CSP would break it.
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        // Baseline headers still apply everywhere.
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
    }
}
