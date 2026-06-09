using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Security.Claims;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Base class for all SharpMUSH REST API controllers.
/// Applies JWT bearer authentication, standardises the response envelope (<see cref="ApiResponse{T}"/>),
/// and provides RFC 7807-compliant error helpers.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    // ── Identity helpers ────────────────────────────────────────────────────

    /// <summary>The <c>sub</c> claim (account GUID) from the bearer token, or <see langword="null"/> if absent.</summary>
    protected string? CurrentAccountId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>The <c>unique_name</c> claim from the bearer token, or <see langword="null"/> if absent.</summary>
    protected string? CurrentUsername =>
        User.FindFirstValue(ClaimTypes.Name);

    /// <summary>The <c>character_key</c> claim parsed as <see cref="int"/>, or <see langword="null"/> if absent or non-numeric.</summary>
    protected int? CurrentCharacterKey =>
        int.TryParse(User.FindFirstValue("character_key"), out var k) ? k : null;
    /// <summary>The <c>character_creation_time</c> claim parsed as <see cref="long"/>, or <see langword="null"/> if absent or non-numeric.</summary>
    protected long? CurrentCharacterCreationTime =>
        long.TryParse(User.FindFirstValue("character_creation_time"), out var t) ? t : null;

    // ── Envelope helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Returns HTTP 200 with the value wrapped in a success envelope.
    /// </summary>
    protected OkObjectResult Ok<T>(T value, string? message = null) =>
        base.Ok(ApiResponse<T>.Success(value, message));

    /// <summary>
    /// Returns HTTP 201 Created with the value wrapped in a success envelope and a <paramref name="location"/> header.
    /// </summary>
    protected CreatedResult Created<T>(string location, T value) =>
        base.Created(location, ApiResponse<T>.Success(value));

    /// <summary>
    /// Returns HTTP 204 No Content (no body).
    /// </summary>
    protected new NoContentResult NoContent() => base.NoContent();

    // ── RFC 7807 error helpers ─────────────────────────────────────────────

    /// <summary>Returns HTTP 400 Bad Request as a Problem Details body.</summary>
    protected ObjectResult Problem400(string detail, string? title = null) =>
        Problem(detail: detail, title: title ?? "Bad Request", statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Returns HTTP 401 Unauthorized as a Problem Details body.</summary>
    protected ObjectResult Problem401(string detail = "Authentication required.") =>
        Problem(detail: detail, title: "Unauthorized", statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>Returns HTTP 403 Forbidden as a Problem Details body.</summary>
    protected ObjectResult Problem403(string detail = "You do not have permission to perform this action.") =>
        Problem(detail: detail, title: "Forbidden", statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Returns HTTP 404 Not Found as a Problem Details body.</summary>
    protected ObjectResult Problem404(string detail) =>
        Problem(detail: detail, title: "Not Found", statusCode: StatusCodes.Status404NotFound);

    /// <summary>Returns HTTP 409 Conflict as a Problem Details body.</summary>
    protected ObjectResult Problem409(string detail) =>
        Problem(detail: detail, title: "Conflict", statusCode: StatusCodes.Status409Conflict);

    /// <summary>Returns HTTP 422 Unprocessable Entity as a Problem Details body (semantic validation failures).</summary>
    protected ObjectResult Problem422(string detail) =>
        Problem(detail: detail, title: "Unprocessable Entity", statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>Returns HTTP 500 Internal Server Error as a Problem Details body. Avoid leaking exception messages in production.</summary>
    protected ObjectResult Problem500(string detail = "An unexpected error occurred.") =>
        Problem(detail: detail, title: "Internal Server Error", statusCode: StatusCodes.Status500InternalServerError);

    // Re-expose the base Problem() overload so the helpers above can call it
    // without accidentally shadowing consumer-facing overloads.
    private new ObjectResult Problem(
        string? detail = null,
        string? instance = null,
        int? statusCode = null,
        string? title = null,
        string? type = null) =>
        base.Problem(detail, instance, statusCode, title, type)!;
}

/// <summary>
/// Standard response envelope for all SharpMUSH REST API responses.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public sealed record ApiResponse<T>
{
    /// <summary>Indicates whether the request succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Human-readable status message (optional).</summary>
    public string? Message { get; init; }

    /// <summary>The response payload, present when <see cref="Succeeded"/> is <see langword="true"/>.</summary>
    public T? Data { get; init; }

    // Private ctor — use factory methods
    private ApiResponse() { }

    /// <summary>Creates a successful response.</summary>
    public static ApiResponse<T> Success(T data, string? message = null) =>
        new() { Succeeded = true, Data = data, Message = message };

    /// <summary>Creates a failed response (used by error-handling middleware).</summary>
    public static ApiResponse<T> Fail(string message) =>
        new() { Succeeded = false, Message = message };
}
