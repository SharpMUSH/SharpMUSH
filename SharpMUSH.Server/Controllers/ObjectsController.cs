using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Typed object and attribute API, acting as the authenticated session's character.
///
/// This exists because the portal's Softcode Editor cannot write multi-line attributes through the
/// terminal: that channel is line-delimited, so the editor rewrote every newline as the two
/// characters <c>%r</c> and sent <c>&amp;ATTR #dbref=value</c>. <c>&amp;</c> is RSNoParse for direct
/// input, so the literal <c>%r</c> was what reached the database — the one write path in the server
/// that mangled its input, where <c>@set</c>, <c>&amp;</c> from a queue and package installs all
/// store real newlines.
///
/// Values here travel in a JSON body and are stored verbatim. Permissions are the engine's: every
/// operation goes through <see cref="IAttributeService"/>, the same service the <c>&amp;</c> command
/// calls, which enforces <c>Controls</c> and <c>CanSet</c> itself. There is no portal-role gate
/// beyond being authenticated with a character.
///
/// Routes:
///   GET    api/objects/{dbref}                              — name, type, owner, flags
///   GET    api/objects/{dbref}/attributes[?depth=N]         — visible attributes
///   GET    api/objects/{dbref}/attributes/{name}            — one attribute
///   PUT    api/objects/{dbref}/attributes/{name}            — set  { value }
///   DELETE api/objects/{dbref}/attributes/{name}            — clear
///   POST   api/objects/{dbref}/attributes/{name}/flags      — set flag  { flag }
///   DELETE api/objects/{dbref}/attributes/{name}/flags/{f}  — unset flag
/// </summary>
[ApiController]
[Route("api/objects")]
[Authorize]
public class ObjectsController(
	IMediator mediator,
	IAttributeService attributeService,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	IEngineCommandInvoker commandInvoker,
	IPermissionService permissionService) : ControllerBase
{
	/// <summary>
	/// Attribute-tree levels returned by the listing endpoint when the caller does not ask.
	/// One level is what <c>examine</c> shows and what every in-engine caller of
	/// <see cref="IAttributeService.GetVisibleAttributesAsync"/> uses.
	/// </summary>
	private const int DefaultListDepth = 1;

	/// <summary>Ceiling on <c>?depth=</c>, so a caller cannot ask the server to walk an unbounded tree.</summary>
	private const int MaxListDepth = 10;

	/// <summary>
	/// Creates an object by running the engine's own building command, so quota, zone inheritance,
	/// the <c>OBJECT`CREATE</c> event and plugin hooks all happen exactly as they do in-game. The
	/// name is handed over as a pre-split argument and never spliced into a command line.
	/// </summary>
	[HttpPost]
	public async Task<IActionResult> CreateObject([FromBody] CreateObjectRequest request, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();

		var arguments = new Dictionary<string, CallState> { ["0"] = new CallState(request.Name) };

		string command;
		switch (request.Type?.ToUpperInvariant())
		{
			case "THING":
				command = "@CREATE";
				break;
			case "ROOM":
				command = "@DIG";
				break;
			case "EXIT":
				command = "@OPEN";
				if (!string.IsNullOrWhiteSpace(request.Destination))
				{
					arguments["1"] = new CallState(request.Destination);
				}

				break;
			default:
				return BadRequest(new ApiErrorDto(
					$"'{request.Type}' is not a creatable type. Use THING, ROOM or EXIT; players are " +
					"created through account character creation."));
		}

		var result = await commandInvoker.InvokeAsync(command, executor.Object().DBRef, arguments);
		var message = result?.Message?.ToPlainText();

		if (string.IsNullOrWhiteSpace(message))
		{
			return StatusCode(StatusCodes.Status500InternalServerError,
				new ApiErrorDto($"{command} returned no result."));
		}

		// Building commands report refusals as a '#-1 …' CallState rather than throwing.
		return message.StartsWith("#-1", StringComparison.Ordinal)
			? StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(message))
			: Ok(new CreatedObjectDto(message));
	}

	[HttpGet("{dbref:int}")]
	public async Task<IActionResult> GetObject(int dbref, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (await ResolveTargetAsync(dbref, ct) is not { } target) return NotFound();

		// The attribute endpoints inherit their visibility check from IAttributeService; this one
		// returns name, owner and flags directly, so it has to ask. Without it any authenticated
		// character could enumerate the database by dbref.
		if (!await permissionService.CanExamine(executor, target))
		{
			return StatusCode(StatusCodes.Status403Forbidden,
				new ApiErrorDto(ErrorMessages.Returns.PermissionDenied));
		}

		return Ok(await SummariseAsync(target, ct));
	}

	[HttpGet("{dbref:int}/attributes")]
	public async Task<IActionResult> ListAttributes(int dbref, [FromQuery] int? depth, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (await ResolveTargetAsync(dbref, ct) is not { } target) return NotFound();

		var requested = Math.Clamp(depth ?? DefaultListDepth, 1, MaxListDepth);
		var result = await attributeService.GetVisibleAttributesAsync(executor, target, requested);

		return result.Match<IActionResult>(
			attributes => Ok(attributes.Select(ToDto).ToList()),
			error => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value)));
	}

	[HttpGet("{dbref:int}/attributes/{name}")]
	public async Task<IActionResult> GetAttribute(int dbref, string name, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (await ResolveTargetAsync(dbref, ct) is not { } target) return NotFound();

		// parent: false — an editor must show what is stored on THIS object. Rendering an
		// inherited value and then saving it would silently copy the parent's code down.
		var result = await attributeService.GetAttributeAsync(
			executor, target, name, IAttributeService.AttributeMode.Read, parent: false);

		return result.Match<IActionResult>(
			attributes => Ok(ToDto(attributes.Last())),
			_ => NotFound(),
			error => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value)));
	}

	[HttpPut("{dbref:int}/attributes/{name}")]
	public async Task<IActionResult> SetAttribute(
		int dbref, string name, [FromBody] SetAttributeRequest request, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (await ResolveTargetAsync(dbref, ct) is not { } target) return NotFound();

		// '& attr obj=' with an empty value clears unless empty_attrs is on — the same fork the
		// command takes, so the two paths cannot disagree about what an empty save means.
		if (string.IsNullOrEmpty(request.Value) && !configuration.CurrentValue.Attribute.EmptyAttributes)
		{
			return await ClearAttributeAsync(executor, target, name);
		}

		var result = await attributeService.SetAttributeAsync(
			executor, target, name, MModule.single(request.Value));

		return result.Match<IActionResult>(
			_ => NoContent(),
			error => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value)));
	}

	[HttpDelete("{dbref:int}/attributes/{name}")]
	public async Task<IActionResult> ClearAttribute(int dbref, string name, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (await ResolveTargetAsync(dbref, ct) is not { } target) return NotFound();

		return await ClearAttributeAsync(executor, target, name);
	}

	[HttpPost("{dbref:int}/attributes/{name}/flags")]
	public async Task<IActionResult> SetAttributeFlag(
		int dbref, string name, [FromBody] SetAttributeFlagRequest request, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (await ResolveTargetAsync(dbref, ct) is not { } target) return NotFound();

		var result = await attributeService.SetAttributeFlagAsync(executor, target, name, request.Flag);

		return result.Match<IActionResult>(
			_ => NoContent(),
			error => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value)));
	}

	[HttpDelete("{dbref:int}/attributes/{name}/flags/{flag}")]
	public async Task<IActionResult> UnsetAttributeFlag(int dbref, string name, string flag, CancellationToken ct)
	{
		if (await ResolveExecutorAsync(ct) is not { } executor) return Unauthorized();
		if (await ResolveTargetAsync(dbref, ct) is not { } target) return NotFound();

		var result = await attributeService.UnsetAttributeFlagAsync(executor, target, name, flag);

		return result.Match<IActionResult>(
			_ => NoContent(),
			error => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value)));
	}

	private async Task<IActionResult> ClearAttributeAsync(AnySharpObject executor, AnySharpObject target, string name)
	{
		var result = await attributeService.ClearAttributeAsync(
			executor, target, name,
			IAttributeService.AttributePatternMode.Exact);

		return result.Match<IActionResult>(
			_ => NoContent(),
			error => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(error.Value)));
	}

	/// <summary>
	/// The character this request acts as. Same rule as <c>MailController</c>: the
	/// <c>character_dbref</c> claim, which <c>AuthController.SwitchCharacter</c> keeps in step with
	/// the terminal's own character.
	/// </summary>
	private async Task<AnySharpObject?> ResolveExecutorAsync(CancellationToken ct)
	{
		if (User.GetActingCharacter() is not { } character) return null;

		var result = await mediator.Send(new GetObjectNodeQuery(character), ct);
		return result.IsNone ? null : result.Known;
	}

	/// <summary>
	/// Addressing is by dbref only. Name resolution needs a parser and notifies on failure, and the
	/// editor already picks its target out of the object browser as a dbref.
	/// </summary>
	private async Task<AnySharpObject?> ResolveTargetAsync(int dbref, CancellationToken ct)
	{
		var result = await mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)), ct);
		return result.IsNone ? null : result.Known;
	}

	private static AttributeDto ToDto(SharpAttribute attribute) => new(
		attribute.LongName ?? attribute.Name,
		attribute.Value.ToPlainText(),
		attribute.Flags.Select(f => f.Name).ToList());

	private static async Task<ObjectSummaryDto> SummariseAsync(AnySharpObject target, CancellationToken ct)
	{
		var obj = target.Object();
		var owner = await obj.Owner.WithCancellation(ct);
		var flags = await obj.Flags.Value.Select(f => f.Name).ToListAsync(ct);

		return new ObjectSummaryDto(
			$"#{obj.Key}",
			obj.Name,
			obj.Type.ToUpperInvariant(),
			$"{owner.Object.Name}(#{owner.Object.Key})",
			flags);
	}
}
