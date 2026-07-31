using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Vanadium.Note.REST.Auth;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Middleware;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Security;
using Vanadium.Note.REST.Services;

var builder = WebApplication.CreateBuilder(args);

// Server-local password-hash generation (issue #296): the `--hash-password` startup flag prints a
// storage hash and exits (handled right after Build below). Silence routine logging first so stdout
// carries ONLY the hash — the Serilog console sink writes to stdout, and the breach-check HttpClient
// would otherwise emit Information logs there and corrupt a `> hash.txt` capture.
var hashPasswordCli = PasswordHashCli.IsInvocation(args);
if (hashPasswordCli)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Fatal",
    });
}

builder.Host.UseSerilog((context, services, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();

    var seqUrl = context.Configuration["Seq:ServerUrl"];
    var seqApiKey = context.Configuration["Seq:ApiKey"];
    if (!string.IsNullOrWhiteSpace(seqUrl) && !string.IsNullOrWhiteSpace(seqApiKey))
        config.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
});


// TLS terminates at an upstream reverse proxy (nginx etc.); the app itself
// serves HTTP inside the container. Trust the proxy's X-Forwarded-For /
// X-Forwarded-Proto so the real client IP and request scheme are restored
// (also fixes the login rate limiter's per-IP partitioning). Which proxies are
// trusted is controlled by ForwardedHeaders:KnownProxies / KnownNetworks — see
// ForwardedHeadersConfigurator for why unconfigured means "keep loopback defaults"
// rather than "trust every hop" (issue #197).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    ForwardedHeadersConfigurator.Configure(options, builder.Configuration);
});

var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? ["http://localhost:7700", "https://localhost:7701"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddDbContext<NoteDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null));
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

var jwtSecret = JwtSecretValidator.Validate(builder.Configuration["Auth:JwtSecret"]);

const string smartScheme = "Smart";

builder.Services.AddAuthentication(smartScheme)
    .AddPolicyScheme(smartScheme, "JWT or PAT", options =>
    {
        // Route personal access tokens to the PAT handler and everything else to JWT.
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            return authHeader.StartsWith($"Bearer {ApiTokenService.TokenPrefix}", StringComparison.Ordinal)
                ? ApiTokenAuthHandler.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthHandler>(ApiTokenAuthHandler.SchemeName, null)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning(
                    "JWT validation failed [{ExceptionType}] {Path}: {Message}",
                    context.Exception.GetType().Name,
                    context.HttpContext.Request.Path,
                    context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddRateLimiter(options =>
{
    // Partition the login limiter by client IP so a single (anonymous) IP cannot
    // exhaust a shared global bucket and lock out the legitimate owner. Each IP gets
    // its own 10 req/min fixed window. Behind a proxy the real client IP is restored
    // by UseForwardedHeaders (configured above, runs first in the pipeline).
    options.AddPolicy("login", context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
    });
    // The anonymous share read endpoint is partitioned by client IP so a single IP cannot
    // brute-force tokens or flood the DB. 60 req/min is ample for opening shared notes (the
    // endpoint returns note metadata + HTML only; embedded assets are not served here).
    options.AddPolicy("share", context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 60,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddControllers();
// Unify error responses on RFC 7807 ProblemDetails: controllers already emit
// ProblemDetails via Problem()/ValidationProblem(), and this registration lets the
// global exception handler below render the same shape through IProblemDetailsService.
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IHtmlSanitizerService, HtmlSanitizerService>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<NoteLifecycleService>();
builder.Services.AddScoped<NoteShareService>();
builder.Services.AddScoped<PropertyService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ApiTokenService>();
builder.Services.AddScoped<FileCleanupService>();
// Singleton: holds first-observed-unreferenced timestamps across periodic scans so the
// orphan grace window is measured from first sighting, not upload time (issue #301).
builder.Services.AddSingleton<OrphanReferenceTracker>();
builder.Services.AddScoped<AccountService>();

builder.Services.Configure<PasswordPolicyOptions>(
    builder.Configuration.GetSection(PasswordPolicyOptions.SectionName));
builder.Services.AddHttpClient<IPwnedPasswordsClient, PwnedPasswordsClient>(client =>
{
    client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
    client.Timeout = TimeSpan.FromSeconds(5);
    // HIBP requires a descriptive User-Agent; requests without one are rejected.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Vanadium.Note.REST");
});
builder.Services.AddScoped<IPasswordValidator, PasswordValidator>();

// Global (IP-independent) login backoff. Complements the per-IP "login" rate limiter:
// the singleton throttle shares one failure counter across all sources so distributed
// IPs / forged X-Forwarded-For (issue #197) cannot dodge the cap. TimeProvider.System is
// injected so the throttle's clock is fake-able in unit tests.
builder.Services.Configure<LoginLockoutOptions>(
    builder.Configuration.GetSection(LoginLockoutOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILoginThrottle, LoginThrottle>();

// Global (IP-independent) PAT authentication backoff. The PAT handler runs during
// authentication and issues one ApiTokens lookup per request, and is not covered by the
// per-IP "login" rate limiter, so a flood of invalid tokens could hammer the DB. The
// singleton throttle shares one failure counter across all sources and, once locked,
// short-circuits attempts before the DB lookup.
builder.Services.Configure<ApiTokenThrottleOptions>(
    builder.Configuration.GetSection(ApiTokenThrottleOptions.SectionName));
builder.Services.AddSingleton<IApiTokenThrottle, ApiTokenThrottle>();

builder.Services.AddHostedService<OrphanFileCleanupJob>();
builder.Services.AddHostedService<RecycleBinPurgeJob>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT token returned by /api/auth/login (without the 'Bearer ' prefix)."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = new List<string>()
    });
});

var app = builder.Build();

// Server-local password-hash generation (issue #296): the `--hash-password` startup flag reads a
// new password from stdin, validates it against the API's password policy, prints its PBKDF2
// storage hash, and exits WITHOUT starting the web host or touching the database. This is the
// production-safe rotation path — `/api/auth/hash` stays Development-only, and requiring shell
// access to the running server is itself the authorization control. Reusing the DI-built
// IPasswordValidator keeps the CLI's policy identical to the endpoint's.
if (hashPasswordCli)
{
    using var cliScope = app.Services.CreateScope();
    var passwordValidator = cliScope.ServiceProvider.GetRequiredService<IPasswordValidator>();
    return await PasswordHashCli.RunAsync(
        passwordValidator, Console.In, Console.Out, Console.Error);
}

var seqConfigUrl = app.Configuration["Seq:ServerUrl"];
var seqConfigKey = app.Configuration["Seq:ApiKey"];
if (!string.IsNullOrWhiteSpace(seqConfigUrl) && string.IsNullOrWhiteSpace(seqConfigKey))
    app.Logger.LogWarning(
        "Seq URL is configured ({SeqUrl}) but SEQ_API_KEY is not set — Seq logging is disabled. " +
        "Set SEQ_API_KEY to enable authenticated log ingestion.",
        seqConfigUrl);

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (feature?.Error is not null)
            logger.LogError(feature.Error, "Unhandled exception: {Method} {Path}",
                context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // Respond with RFC 7807 ProblemDetails, matching the controllers' error contract.
        // The message is deliberately generic — internal exception details are logged, not exposed.
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            }
        });

        // Fallback for the rare case the writer declines (e.g. no acceptable content type):
        // still emit a ProblemDetails body so the error contract holds unconditionally.
        if (!written)
        {
            await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            }, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json");
        }
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Skip the relational migration step under the integration-test host: the smoke
// E2E factory swaps the Npgsql provider for in-memory SQLite (which cannot run
// Npgsql migrations) and creates the schema itself. Every real environment
// (Development/Production) still migrates on startup as before.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<NoteDbContext>();
    startupLogger.LogInformation("Applying database migrations...");
    db.Database.Migrate();
    startupLogger.LogInformation("Database migrations applied.");

    // One-time legacy content re-sanitize backfill (issue #294). The re-sanitize itself cannot
    // run inside the EF migration because HtmlSanitizer is C#, not SQL, so it runs here right
    // after Migrate() and is gated by a persisted flag on the UserSettings singleton — this makes
    // it fire exactly once (crash-safe: the flag is only set after the backfill's own commit).
    var settings = await db.UserSettings.FirstOrDefaultAsync();
    if (settings is null)
    {
        settings = new UserSettings { Id = Guid.NewGuid() };
        db.UserSettings.Add(settings);
    }
    if (!settings.LegacyContentSanitized)
    {
        var noteService = scope.ServiceProvider.GetRequiredService<NoteService>();
        var updated = await noteService.ReSanitizeAllContentAsync();
        settings.LegacyContentSanitized = true;
        await db.SaveChangesAsync();
        startupLogger.LogInformation(
            "Legacy content re-sanitize backfill complete; {UpdatedCount} note(s) updated.", updated);
    }
}

// Must run before any middleware that reads the client IP or request scheme
// (rate limiter, logging, HSTS) so they observe the values the proxy forwarded.
app.UseForwardedHeaders();

// HSTS instructs browsers to stick to HTTPS. Skipped in Development (where the
// app is hit over plain HTTP) and emitted only once ForwardedHeaders has
// restored scheme=https from the proxy. HTTPS *redirection* is deliberately NOT
// enabled here: the app is HTTP-only inside the container and redirection is the
// reverse proxy's responsibility — UseHttpsRedirection would need an HTTPS port
// and would otherwise cause redirect loops behind the proxy.
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

// app.Run() blocks until shutdown; on graceful exit the process returns 0. An explicit return is
// required because the `--hash-password` branch above returns an int, making this entry point
// int-typed on every path.
return 0;

// Exposes the implicit top-level Program class to the test assembly so the smoke
// E2E WebApplicationFactory<Program> can bootstrap the real application pipeline.
public partial class Program;
