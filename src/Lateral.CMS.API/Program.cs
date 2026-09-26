using Asp.Versioning;
using Lateral.CMS.API.Common;
using Lateral.CMS.API.Configuration;
using Lateral.CMS.API.HostedServices;
using Lateral.CMS.API.Security;
using Lateral.CMS.Application.Security;
using Lateral.CMS.Domain.Constants;
using Lateral.CMS.Infrastructure.Data.SqlServer;
using Lateral.CMS.Infrastructure.IoC;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
using System.Text.Json;
using System.Text.Json.Serialization;

// Start-up can fail before the configured providers exist — a bad configuration section, a missing
// connection string — so a plain console logger covers that window.
using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Lateral.CMS.API");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // One provider, chosen explicitly, instead of the four the host adds by default.
    builder.Logging.ClearProviders();

    if (builder.Environment.IsDevelopment())
    {
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";

            // Scopes stay off here: the framework's own (SpanId, TraceId, ConnectionId) are prepended to
            // every line and bury the message. The correlation identifier is still on the request line,
            // and a structured collector gets all of them from the JSON formatter below.
            options.IncludeScopes = false;
        });
    }
    else
    {
        // Structured output for a log collector. Scopes carry the correlation identifier, so every line
        // written while a request runs can be tied back to it.
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });
    }

    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            // Enumerations travel as names, so "Unpublished" survives a renumbering and reads in the logs.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    builder.Services
        .AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

    // Every entity is treated as confidential, so authentication is wired before anything is routed.
    builder.Services.Configure<BasicAuthenticationOptions>(builder.Configuration.GetSection(BasicAuthenticationOptions.SectionName));
    builder.Services.AddSingleton<BasicAuthenticationCredentialStore>();

    builder.Services
        .AddAuthentication(BasicAuthenticationDefaults.AuthenticationScheme)
        .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
            BasicAuthenticationDefaults.AuthenticationScheme, configureOptions: null);

    builder.Services.AddAuthorizationBuilder()
        // Nothing is public unless it opts out explicitly: a new endpoint is closed by default.
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddPolicy(Policies.CmsIngestion, policy => policy.RequireRole(Roles.Organization))
        .AddPolicy(Policies.ContentReader, policy => policy.RequireRole(Roles.User, Roles.Admin))
        .AddPolicy(Policies.ContentAdministrator, policy => policy.RequireRole(Roles.Admin));

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

    builder.Services.AddHostedService<CmsEventProcessorHostedService>();

    // One log line per request instead of the pair HTTP logging writes by default.
    builder.Services.AddHttpLogging(options =>
    {
        options.LoggingFields = HttpLoggingFields.RequestMethod
            | HttpLoggingFields.RequestPath
            | HttpLoggingFields.ResponseStatusCode
            | HttpLoggingFields.Duration;

        options.CombineLogs = true;
    });

    builder.Services.AddHttpLoggingInterceptor<CmsHttpLoggingInterceptor>();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BasicAuthenticationDocumentTransformer>());

    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

    // Model binding failures never reach a handler, so they are shaped here to match the problem details
    // the rest of the API returns.
    builder.Services.Configure<ApiBehaviorOptions>(options =>
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    new ApiError("request.malformed", $"'{entry.Key}' is invalid.", error.ErrorMessage)))
                .ToList();

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request is not valid.",
                Instance = context.HttpContext.Request.Path
            };

            problem.Extensions["errors"] = errors;
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

            return new BadRequestObjectResult(problem);
        });

    var app = builder.Build();

    // A misconfigured credential section would either lock every caller out or, worse, accept an unintended
    // one. Fail at start-up instead.
    var credentialErrors = app.Services.GetRequiredService<BasicAuthenticationCredentialStore>().Validate();

    if (credentialErrors.Count > 0)
        throw new InvalidOperationException(
            $"The section '{BasicAuthenticationOptions.SectionName}' is not usable: {string.Join(" ", credentialErrors)}");

    app.UseExceptionHandler();
    app.UseMiddleware<CorrelationIdMiddleware>();

    app.UseHttpLogging();

    if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
    {
        // Brings a fresh database up to date, so the API is usable straight after start.
        await app.MigrateDatabaseAsync<CmsDbContext>(
            builder.Configuration.GetValue("Database:MigrationTimeout", TimeSpan.FromMinutes(2)));
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // The description of the API is not CMS data, but it is only published outside production.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference(options => options.WithTitle("Lateral CMS API")).AllowAnonymous();
    }

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

    app.MapControllers();

    app.Logger.LogInformation("Lateral CMS API starting in {Environment}.", app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    bootstrapLogger.LogCritical(exception, "The API terminated unexpectedly.");
    throw;
}

/// <summary>Exposed so the integration tests can host the API in memory.</summary>
public partial class Program;
