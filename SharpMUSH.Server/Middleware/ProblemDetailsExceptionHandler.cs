using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server.Helpers;
using System.Text.Json;

namespace SharpMUSH.Server.Middleware;

/// <summary>
/// Global exception handler that converts unhandled exceptions into RFC 7807 Problem Details responses.
/// Registered via <c>app.UseExceptionHandler()</c>; replaces <c>app.UseDeveloperExceptionPage()</c>
/// in production while still producing structured JSON in all environments.
/// </summary>
public sealed class ProblemDetailsExceptionHandler(
    ILogger<ProblemDetailsExceptionHandler> logger,
    IHostEnvironment env) : IExceptionHandler
{
    // RFC 7807 §3 mandates "application/problem+json" for Problem Details responses.
    private const string ProblemJsonContentType = "application/problem+json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // W-2: Client disconnects are not server errors. Respond with 499 Client Closed
        // Request (nginx convention) and log at Debug — not Error.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request cancelled by client on {Method} {Path}",
                LogSanitizer.Sanitize(httpContext.Request.Method), LogSanitizer.Sanitize(httpContext.Request.Path));
            httpContext.Response.StatusCode = 499;
            return true;
        }

        logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            LogSanitizer.Sanitize(httpContext.Request.Method), LogSanitizer.Sanitize(httpContext.Request.Path));

        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            KeyNotFoundException         => (StatusCodes.Status404NotFound, "Not Found"),
            ArgumentException           => (StatusCodes.Status400BadRequest, "Bad Request"),
            InvalidOperationException   => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity"),
            _                           => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = env.IsDevelopment() ? exception.Message : "An unexpected error occurred.",
            Instance = httpContext.Request.Path,
        };

        httpContext.Response.StatusCode = status;
        // C-2: RFC 7807 §3 requires application/problem+json, not application/json.
        httpContext.Response.ContentType = ProblemJsonContentType;
        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(problem, JsonOptions),
            cancellationToken);

        return true;
    }
}
