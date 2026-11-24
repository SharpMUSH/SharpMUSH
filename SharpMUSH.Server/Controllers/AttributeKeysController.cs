using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The attribute table's read and write keys, acting as the session's character.
///
/// Routes:
///   GET  api/admin/attribute-keys               — every entry that holds a key
///   PUT  api/admin/attribute-keys/{name}/{kind} — set or clear its read or write key { key }
/// </summary>
[ApiController]
[Route("api/admin/attribute-keys")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class AttributeKeysController(
	IMediator mediator,
	IEngineCommandInvoker commandInvoker,
	IVisibleWorldProjection projection,
	IBooleanExpressionParser booleanExpressionParser,
	ICommandOutputCapture outputCapture) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List(CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor) return Unauthorized();

		var entries = new List<AttributeKeyEntryDto>();
		await foreach (var entry in mediator.CreateStream(new GetAllAttributeEntriesQuery(), ct))
		{
			if (entry.ReadLock is null && entry.WriteLock is null) continue;
			entries.Add(new AttributeKeyEntryDto(entry.Name,
				entry.ReadLock is { } read ? await booleanExpressionParser.ReadbackAsync(read, executor, ct) : null,
				entry.WriteLock is { } write ? await booleanExpressionParser.ReadbackAsync(write, executor, ct) : null));
		}

		Response.Headers.CacheControl = "no-store";
		return Ok(entries.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList());
	}

	/// <summary>
	/// Runs <c>@attribute/lock/read</c> or <c>/write</c>, so the binding and the permission check are the command's own.
	/// The key is a pre-split argument and is never evaluated.
	/// </summary>
	[HttpPut("{name}/{kind}")]
	public async Task<IActionResult> SetKey(string name, string kind, [FromBody] SetAttributeKeyRequest request,
		CancellationToken ct)
	{
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor) return Unauthorized();

		var keySwitch = kind.ToUpperInvariant() switch { "READ" => "READ", "WRITE" => "WRITE", _ => null };
		if (keySwitch is null) return BadRequest(new ApiErrorDto("Name the read or the write key."));

		var arguments = new Dictionary<string, CallState>
		{
			["0"] = new CallState(name),
			["1"] = new CallState(request.Key ?? string.Empty)
		};
		// What the command told the executor is the refusal's reason ("I don't understand that key."), where its result
		// is only a #-1 string.
		var told = new CommandTranscript(4096);
		CallState? result;
		using (outputCapture.BeginCapture(executor.Object().DBRef.Number, told))
		{
			result = await commandInvoker.InvokeAsync("@ATTRIBUTE", executor.Object().DBRef, arguments, ["LOCK", keySwitch]);
		}

		if (result is null)
		{
			return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorDto("@ATTRIBUTE returned no result."));
		}

		var message = result.Message.ToPlainText();
		var reason = told.Lines.LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? message;
		return message switch
		{
			ErrorMessages.Returns.NotFound => NotFound(new ApiErrorDto(reason)),
			ErrorMessages.Returns.InvalidValue => BadRequest(new ApiErrorDto(reason)),
			_ when message.StartsWith("#-1", StringComparison.Ordinal) => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(reason)),
			_ => NoContent()
		};
	}
}
