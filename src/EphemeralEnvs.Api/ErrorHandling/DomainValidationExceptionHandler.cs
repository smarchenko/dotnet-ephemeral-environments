using EphemeralEnvs.Domain;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EphemeralEnvs.Api.ErrorHandling;

/// <summary>
/// A violated domain invariant is a client error: returns 400 ValidationProblemDetails with the field's error,
/// the same shape as the automatic model-validation response.
/// </summary>
internal sealed class DomainValidationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainValidationException validation)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [validation.Field] = [validation.Message],
            })
            {
                Status = StatusCodes.Status400BadRequest,
            },
        });
    }
}
