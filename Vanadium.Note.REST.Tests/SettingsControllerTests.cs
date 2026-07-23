using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.REST.Controllers;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Security;
using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Owner-password re-confirmation on the all-data purge (issue #289). The JWT-only scheme
/// restriction is an [Authorize] attribute exercised end-to-end in
/// <see cref="SmokeE2E.SettingsPurgeAuthE2ETests"/>; here the in-controller password gate is
/// tested directly, mirroring <see cref="ArchiveControllerTests"/>.
/// </summary>
public class SettingsControllerTests
{
    private const string OwnerPassword = "correct horse battery staple";

    private static SettingsController CreateController(TestHost h, string? passwordHash)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:PasswordHash"] = passwordHash })
            .Build();
        var controller = new SettingsController(
            new SettingsService(h.Db), h.Account, config, NullLogger<SettingsController>.Instance)
        {
            ControllerContext = CreateContext()
        };
        return controller;
    }

    // A minimal context with RequestServices so ControllerBase.Problem() can resolve
    // ProblemDetailsFactory. There is no user identity in the single-user model.
    private static ControllerContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, AuthController.OwnerName)], "Test")),
            RequestServices = services.BuildServiceProvider()
        };
        return new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task DeleteAllData_WithWrongPassword_Returns403AndKeepsData()
    {
        using var h = new TestHost();
        await h.CreateNoteAsync("Keep me");
        var controller = CreateController(h, PasswordHasher.Hash(OwnerPassword));

        var result = await controller.DeleteAllData(new DeleteAllDataRequest("wrong password"));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.NotEmpty(await h.Db.Notes.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task DeleteAllData_WithCorrectPassword_Returns204AndPurges()
    {
        using var h = new TestHost();
        await h.CreateNoteAsync("Delete me");
        var controller = CreateController(h, PasswordHasher.Hash(OwnerPassword));

        var result = await controller.DeleteAllData(new DeleteAllDataRequest(OwnerPassword));

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await h.Db.Notes.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task DeleteAllData_WithoutConfiguredHash_Returns500()
    {
        using var h = new TestHost();
        var controller = CreateController(h, passwordHash: null);

        var result = await controller.DeleteAllData(new DeleteAllDataRequest(OwnerPassword));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }
}
