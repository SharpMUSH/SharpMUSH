using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using MarkupString;
using System.Security.Claims;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Staff administration for the character-profile http_handler (Wizard+).
/// Lets admins inspect the configured handler and (re)install the stock
/// <c>HTTP`PROFILE`*</c> softcode.
///
/// Routes:
///   GET  /api/admin/profile-handler        — handler dbref/name + per-attribute presence
///   POST /api/admin/profile-handler/reset  — overwrite all stock attributes with defaults
/// </summary>
[ApiController]
[Route("api/admin/profile-handler")]
[Authorize(Roles = nameof(PortalRole.Wizard))]
public class ProfileAdminController(
	IMediator mediator,
	IAttributeService attributeService,
	IHttpHandlerDispatcher dispatcher,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<ProfileAdminController> logger) : ControllerBase
{
	private const string SchemaAttribute = "HTTP`PROFILE`SCHEMA";

	/// <param name="Present">Whether the attribute exists on the handler.</param>
	/// <param name="ValidJson">For dry-run routes: whether the attribute evaluates to valid JSON (null = not dry-run).</param>
	/// <param name="Error">The validation error (empty / parse message) when <c>ValidJson</c> is false.</param>
	/// <param name="Preview">A truncated preview of the attribute's evaluated output.</param>
	public record AttributeStatusDto(string Attribute, bool Present, bool? ValidJson = null, string? Error = null, string? Preview = null);

	public record HandlerStatusDto(
		bool Configured,
		int? HandlerDbref,
		string? HandlerName,
		bool HandlerExists,
		IReadOnlyList<AttributeStatusDto> Attributes);

	public record ResetResultDto(int Written, IReadOnlyList<string> Failed);

	[HttpGet]
	public async Task<ActionResult<HandlerStatusDto>> Status(CancellationToken ct)
	{
		var handlerDbRef = options.CurrentValue.Database.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			return new HandlerStatusDto(false, null, null, false, []);
		}

		var handlerResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRef.Value, null)), ct);
		if (handlerResult.IsNone)
		{
			return new HandlerStatusDto(true, (int)handlerDbRef.Value, null, false, []);
		}

		var handler = handlerResult.Known;
		var viewer = Viewer();
		var statuses = new List<AttributeStatusDto>();
		foreach (var (attribute, _) in DefaultProfileHandlerSoftcode.Attributes)
		{
			var existing = await attributeService.GetAttributeAsync(
				handler, handler, attribute, IAttributeService.AttributeMode.Execute, parent: false);
			var present = existing.IsAttribute;

			// Dry-run the schema route (read-only and viewer-independent) so admins can see whether
			// it actually evaluates to valid JSON — the exact failure that crashes the portal when
			// the handler softcode returns empty or a "#-1 ..." error.
			if (present && attribute == SchemaAttribute)
			{
				var dispatch = await dispatcher.DispatchAsync(
					attribute, "GET", "/profile-schema", string.Empty, string.Empty, viewer, ct);
				var (valid, error, preview) = dispatch.Match<(bool?, string?, string?)>(
					body => string.IsNullOrWhiteSpace(body)
						? (false, "Handler returned an empty response.", string.Empty)
						: ValidateJson(body),
					_ => ((bool?)false, "Handler route not available (404).", null));
				statuses.Add(new AttributeStatusDto(attribute, present, valid, error, preview));
			}
			else
			{
				statuses.Add(new AttributeStatusDto(attribute, present));
			}
		}

		return new HandlerStatusDto(true, (int)handlerDbRef.Value, handler.Object().Name, true, statuses);
	}

	/// <summary>Parses <paramref name="body"/> as JSON, returning validity, any error, and a truncated preview.</summary>
	private static (bool? Valid, string? Error, string? Preview) ValidateJson(string body)
	{
		var preview = body.Length > 300 ? body[..300] + "…" : body;
		try
		{
			using var _ = System.Text.Json.JsonDocument.Parse(body);
			return (true, null, preview);
		}
		catch (System.Text.Json.JsonException ex)
		{
			return (false, ex.Message, preview);
		}
	}

	/// <summary>The requesting admin's character dbref from the JWT; <c>#-1</c> when absent.</summary>
	private DBRef Viewer()
	{
		var raw = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return new DBRef(-1, null);
		}

		var numberPart = raw.TrimStart('#').Split(':', 2)[0];
		return int.TryParse(numberPart, out var number) ? new DBRef(number, null) : new DBRef(-1, null);
	}

	[HttpPost("reset")]
	public async Task<ActionResult<ResetResultDto>> Reset(CancellationToken ct)
	{
		var handlerDbRef = options.CurrentValue.Database.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			return BadRequest(new { error = "No http_handler is configured." });
		}

		var handlerResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRef.Value, null)), ct);
		if (handlerResult.IsNone)
		{
			return NotFound(new { error = $"Configured http_handler #{handlerDbRef.Value} not found." });
		}

		var godResult = await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)), ct);
		if (godResult.IsNone)
		{
			return StatusCode(500, new { error = "God (#1) not found." });
		}

		var handler = handlerResult.Known;
		var god = godResult.Known;
		var written = 0;
		var failed = new List<string>();

		// Overwrite (not skip-if-present): this is the explicit "reset to defaults" action.
		foreach (var (attribute, code) in DefaultProfileHandlerSoftcode.Attributes)
		{
			var setResult = await attributeService.SetAttributeAsync(god, handler, attribute, MModule.single(code));
			if (setResult.IsT1)
			{
				failed.Add(attribute);
				logger.LogWarning("Reset failed for {Attribute} on #{HandlerDbRef}: {Error}",
					attribute, handlerDbRef.Value, setResult.AsT1.Value);
			}
			else
			{
				written++;
			}
		}

		logger.LogInformation("Profile handler reset on #{HandlerDbRef}: {Written} written, {Failed} failed.",
			handlerDbRef.Value, written, failed.Count);
		return new ResetResultDto(written, failed);
	}
}
