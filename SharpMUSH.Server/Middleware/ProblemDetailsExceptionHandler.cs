using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Mime;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            KeyNotFoundException         => (StatusCodes.Status404NotFound, "Not Found"),
            ArgumentException           => (StatusCodes.Status400BadRequest, "Bad Request"),
            InvalidOperationException   => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity"),
            OperationCanceledException  => (StatusCodes.Status408RequestTimeout, "Request Timeout"),
            _                           => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // In development expose the full exception message; in production use a generic string.
            Detail = env.IsDevelopment() ? exception.Message : "An unexpected error occurred.",
            Instance = httpContext.Request.Path,
        };

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = MediaTypeNames.Application.Json;
        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(problem, JsonOptions),
            cancellationToken);

        return true;
    }
}
