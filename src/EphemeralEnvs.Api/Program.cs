using EphemeralEnvs.Api.ErrorHandling;
using EphemeralEnvs.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Errors (4xx/5xx, validation) are returned as RFC 9457 ProblemDetails (application/problem+json).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// CQRS handlers + DbContexts. The API itself never sees a DbContext.
builder.Services.AddApplication();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the tests.
public partial class Program;
