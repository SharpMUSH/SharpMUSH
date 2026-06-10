using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Security.Claims;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Character-profile API. Thin bridge: every route delegates to the configured in-game
/// <c>http_handler</c> object via <see cref="IHttpHandlerDispatcher"/>, which evaluates the
/// matching <c>HTTP`PROFILE`*</c> MUSHcode attribute. The handler softcode owns the schema,
/// the field-to-attribute mapping, and all per-viewer visibility/edit permission decisions —
/// the engine stays opinionless about what a profile contains.
///
/// Routes:
///   GET  /api/profile-schema      — field/section schema (HTTP`PROFILE`SCHEMA)
///   GET  /api/profile/{name}      — a character's visible field values (HTTP`PROFILE`GET)
///   POST /api/profile/{name}      — update editable fields (HTTP`PROFILE`SET)
/// </summary>
[ApiController]
[Route("api")]
public class ProfileController(
	IHttpHandlerDispatcher dispatcher,
	ILogger<ProfileController> logger) : ControllerBase
{
	private const string SchemaAttribute = "HTTP`PROFILE`SCHEMA";
	private const string GetAttribute = "HTTP`PROFILE`GET";
	private const string SetAttribute = "HTTP`PROFILE`SET";

	[HttpGet("profile-schema")]
	[AllowAnonymous]
	public async Task<IActionResult> GetSchema(CancellationToken ct)
	{
		var result = await dispatcher.DispatchAsync(
			SchemaAttribute, "GET", "/profile-schema", QueryString(), string.Empty, Viewer(), ct);
		return result.Match(Json, _ => HandlerUnavailable());
	}

	[HttpGet("profile/{name}")]
	[AllowAnonymous]
	public async Task<IActionResult> GetProfile(string name, CancellationToken ct)
	{
		var result = await dispatcher.DispatchAsync(
			GetAttribute, "GET", $"/profile/{name}", QueryString(), string.Empty, Viewer(), ct);
		return result.Match(Json, _ => HandlerUnavailable());
	}

	[HttpPost("profile/{name}")]
	[Authorize]
	public async Task<IActionResult> SetProfile(string name, CancellationToken ct)
	{
		using var reader = new StreamReader(Request.Body);
		var body = await reader.ReadToEndAsync(ct);

		var result = await dispatcher.DispatchAsync(
			SetAttribute, "POST", $"/profile/{name}", QueryString(), body, Viewer(), ct);

		// TODO(area-06): publish portal.profile.edit on the message bus and invalidate the
		// profile cache once the profile cache layer exists.
		return result.Match(Json, _ => HandlerUnavailable());
	}

	/// <summary>Resolves the requesting character's dbref from the JWT; <c>#-1</c> when anonymous.</summary>
	private DBRef Viewer()
	{
		var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return new DBRef(-1, null);
		}

		// Claim is a dbref string like "#42" or an objid "#42:timestamp".
		var trimmed = raw.TrimStart('#');
		var numberPart = trimmed.Split(':', 2)[0];
		return int.TryParse(numberPart, out var number) ? new DBRef(number, null) : new DBRef(-1, null);
	}

	private string QueryString() => Request.QueryString.HasValue ? Request.QueryString.Value!.TrimStart('?') : string.Empty;

	private ContentResult Json(string body) => Content(body, "application/json");

	private IActionResult HandlerUnavailable()
	{
		logger.LogDebug("No http_handler route available for the requested profile operation.");
		return NotFound();
	}
}
