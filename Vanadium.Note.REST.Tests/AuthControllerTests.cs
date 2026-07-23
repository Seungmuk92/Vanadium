using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vanadium.Note.REST.Controllers;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Security;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Login must be verified even during an active global lockout, so the owner's correct password
/// always gets through and clears the lock; only a wrong password is refused with 429 while
/// locked (issue #291 — the old skip-verification-while-locked path was an owner DoS).
/// </summary>
public class AuthControllerTests
{
    private const string OwnerPassword = "s3cure-owner-password";
    // 32+ chars so GenerateJwtToken's HS256 signing key is accepted.
    private const string JwtSecret = "auth-controller-tests-jwt-secret-0123456789";

    private static AuthController CreateController(ILoginThrottle throttle)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:PasswordHash"] = PasswordHasher.Hash(OwnerPassword),
                ["Auth:JwtSecret"] = JwtSecret,
            })
            .Build();

        // env and passwordValidator are only used by the dev-only /hash endpoint, never by Login.
        return new AuthController(config, env: null!, passwordValidator: null!, throttle,
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = CreateContext()
        };
    }

    private static ControllerContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return new ControllerContext { HttpContext = httpContext };
    }

    private static LoginThrottle LockedThrottle()
    {
        var throttle = new LoginThrottle(
            Options.Create(new LoginLockoutOptions
            {
                FailureThreshold = 1,
                BaseDelaySeconds = 300,
                MaxDelaySeconds = 900,
            }),
            TimeProvider.System,
            NullLogger<LoginThrottle>.Instance);
        throttle.RegisterFailure(); // threshold = 1 → immediately locked
        Assert.True(throttle.IsLocked(out _));
        return throttle;
    }

    [Fact]
    public void Login_WhileLocked_WithCorrectPassword_SucceedsAndClearsLockout()
    {
        var throttle = LockedThrottle();
        var controller = CreateController(throttle);

        var result = controller.Login(new LoginRequest(OwnerPassword));

        Assert.IsType<OkObjectResult>(result);
        Assert.False(throttle.IsLocked(out _)); // a correct password cleared the lock
    }

    [Fact]
    public void Login_WhileLocked_WithWrongPassword_Returns429AndStaysLocked()
    {
        var throttle = LockedThrottle();
        var controller = CreateController(throttle);

        var result = controller.Login(new LoginRequest("wrong-password"));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
        Assert.True(throttle.IsLocked(out _));
    }

    [Fact]
    public void Login_NotLocked_WithWrongPassword_Returns401()
    {
        var throttle = new LoginThrottle(
            Options.Create(new LoginLockoutOptions { FailureThreshold = 5, BaseDelaySeconds = 300, MaxDelaySeconds = 900 }),
            TimeProvider.System,
            NullLogger<LoginThrottle>.Instance);
        var controller = CreateController(throttle);

        var result = controller.Login(new LoginRequest("wrong-password"));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
    }
}
