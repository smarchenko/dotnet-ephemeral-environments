using EphemeralEnvs.Api.ErrorHandling;
using EphemeralEnvs.Api.Infrastructure;
using EphemeralEnvs.Application;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

const string readyTag = "ready";

var builder = WebApplication.CreateBuilder(args);

// Logs: one JSON object per line on stdout (docker logs, collectors), with the request's TraceId/SpanId.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
builder.Logging.Configure(o => o.ActivityTrackingOptions =
    ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

builder.Services.AddControllers();
// Errors (4xx/5xx, validation) are returned as RFC 9457 ProblemDetails (application/problem+json).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, _) =>
{
    var appOptions = context.ApplicationServices.GetRequiredService<IOptions<AppOptions>>().Value;
    document.Info.Description = $"Environment: {appOptions.EnvironmentName}";
    return Task.CompletedTask;
}));

builder.Services.AddCors(options => options.AddPolicy(CorsSetup.PolicyName, policy => policy
    .WithOrigins(CorsSetup.ParseOrigins(builder.Configuration[CorsSetup.ConfigKey]))
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "OPTIONS")));

// live: the process answers (no dependencies). ready: it can also reach the database.
builder.Services.AddHealthChecks().AddDatabaseCheck(readyTag);

// CQRS handlers + DbContexts. The API itself never sees a DbContext.
builder.Services.AddApplication();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors(CorsSetup.PolicyName);

// A flag, not the ASP.NET Core environment: preview environments run as Production but may show Swagger.
if (app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));
}

app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(readyTag) });
app.MapControllers();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the tests.
public partial class Program;
