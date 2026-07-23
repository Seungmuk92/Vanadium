using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Security;

namespace Vanadium.Note.REST.Tests.SmokeE2E;

/// <summary>
/// Boots the real REST application pipeline in-process (TestServer) for the smoke
/// E2E test, with two production-safe substitutions:
/// <list type="bullet">
///   <item>the Npgsql <see cref="NoteDbContext"/> is swapped for a shared in-memory
///     SQLite database, so the happy path runs deterministically in CI without a
///     PostgreSQL service container;</item>
///   <item>the background hosted jobs are removed, so they cannot race the single
///     in-memory SQLite connection during the test.</item>
/// </list>
/// Every middleware, the JWT/PAT authentication, routing and the controllers are
/// exercised exactly as in a real request — only the database provider differs.
/// </summary>
public sealed class VanadiumWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>The owner password the factory configures a hash for; used by the test to log in.</summary>
    public const string OwnerPassword = "smoke-e2e-owner-password";

    // A 32+ byte secret so JwtSecretValidator (HS256 requires a 256-bit key) accepts it.
    private const string JwtSecret = "smoke-e2e-jwt-secret-value-0123456789-abcdef";

    // Kept open for the factory's lifetime: an in-memory SQLite database only exists
    // while at least one connection to it is open.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    // Program validates Auth:JwtSecret eagerly during WebApplication.CreateBuilder,
    // before the host is built — earlier than any ConfigureAppConfiguration source takes
    // effect. Environment variables are read at that point (the default builder calls
    // AddEnvironmentVariables), so the auth config must be supplied this way.
    //
    // Environment variables are process-global, so this is done ONCE in a static
    // constructor and never torn down. Every E2E factory (there is one per test class,
    // and xUnit runs test classes in parallel) then observes the same valid config while
    // building its host. Setting them per-instance and nulling them in Dispose instead
    // raced across parallel factories: one factory's Dispose could null Auth:JwtSecret
    // while another was still building its host, making that host fail to start
    // ("Auth:JwtSecret is not configured") and flaking a smoke test on CI.
    // "__" maps to the ":" configuration separator.
    static VanadiumWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Auth__JwtSecret", JwtSecret);
        Environment.SetEnvironmentVariable("Auth__PasswordHash", PasswordHasher.Hash(OwnerPassword));
        // Never used (the provider is replaced below) but present so the Npgsql option
        // builder never observes a null connection string while services are configured.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=vanadium_test;Username=test;Password=test");
    }

    public VanadiumWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Drop the Npgsql registration and its options, plus the background jobs.
            // EF Core 9/10 applies the provider via IDbContextOptionsConfiguration<T>,
            // so that descriptor must go too — otherwise the Npgsql config keeps
            // applying alongside SQLite ("only a single provider" error).
            services.RemoveAll<IDbContextOptionsConfiguration<NoteDbContext>>();
            services.RemoveAll<DbContextOptions<NoteDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<NoteDbContext>();
            services.RemoveAll<IHostedService>();

            services.AddDbContext<NoteDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Create the schema on the shared in-memory connection once, before any request.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NoteDbContext>().Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        // Only the per-factory SQLite connection is torn down here. The auth/config
        // environment variables are process-global and set once in the static
        // constructor; they are deliberately NOT nulled, so disposing one factory can
        // never pull valid config out from under another factory that is still building
        // its host on a parallel test thread.
        _connection.Dispose();
    }
}
