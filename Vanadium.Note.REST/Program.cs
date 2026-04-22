using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Middleware;
using Vanadium.Note.REST.Services;

var builder = WebApplication.CreateBuilder(args);

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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

var jwtSecret = builder.Configuration["Auth:JwtSecret"]
    ?? throw new InvalidOperationException("Auth:JwtSecret is not configured. Set Auth:JwtSecret in appsettings.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 10;
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddControllers();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<LabelService>();
builder.Services.AddScoped<FileCleanupService>();
builder.Services.AddHostedService<OrphanFileCleanupJob>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    startupLogger.LogInformation("Applying database migrations...");
    scope.ServiceProvider.GetRequiredService<NoteDbContext>().Database.Migrate();
    startupLogger.LogInformation("Database migrations applied.");
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
