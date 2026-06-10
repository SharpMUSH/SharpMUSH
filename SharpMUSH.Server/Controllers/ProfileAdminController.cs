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
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<ProfileAdminController> logger) : ControllerBase
{
	public record AttributeStatusDto(string Attribute, bool Present);

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
		var statuses = new List<AttributeStatusDto>();
		foreach (var (attribute, _) in DefaultProfileHandlerSoftcode.Attributes)
		{
			var existing = await attributeService.GetAttributeAsync(
				handler, handler, attribute, IAttributeService.AttributeMode.Execute, parent: false);
			statuses.Add(new AttributeStatusDto(attribute, existing.IsAttribute));
		}

		return new HandlerStatusDto(true, (int)handlerDbRef.Value, handler.Object().Name, true, statuses);
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
