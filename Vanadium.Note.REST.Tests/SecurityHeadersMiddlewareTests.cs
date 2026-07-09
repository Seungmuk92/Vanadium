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
}
