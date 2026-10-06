using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The portal's one route for game commands: runs a line as the account session's character and
/// answers with what it produced.
///
/// Routes:
///   POST /api/commands        — { command, result?, character? } → { output, result, truncated }
///   POST /api/commands/eval   — { expression, arguments?, object?, character? } → { output, result, truncated }
///
/// It acts as the character the session is bound to, whatever character a terminal in the same tab
/// is playing. A request that names a <c>character</c> runs only while the session is still bound to
/// it, and is answered 409 otherwise — so a flow of several commands cannot carry on as another
/// character when the tab switches mid-way. An account with too many commands pending is answered 429. It runs anything the character could type, softcode <c>$</c>-commands included, and
/// nothing more: the character's own permissions decide what the command may do.
///
/// <c>eval</c> is the Softcode Editor's console. It evaluates an expression with <c>%0</c>-<c>%9</c> set, as
/// the character or, for an object the character controls, as that object — how the editor runs an
/// attribute's unsaved text the way <c>u()</c> would run it once saved. Running code as an object needs
/// what writing that code onto the object needs, so control is the test.
/// </summary>
[ApiController]
[Route("api/commands")]
[Authorize]
public class CommandsController(
	IPortalCommandService commands,
	IVisibleWorldProjection projection,
	IMediator mediator,
	IPermissionService permissions,
	IRealityPolicy reality) : ControllerBase
{
	/// <summary>The longest line accepted, PennMUSH's <c>BUFFER_LEN</c> for a typed command.</summary>
	public const int MaxCommandLength = 8192;

	/// <summary>The longest expression <c>eval</c> accepts: an attribute's whole text, not one typed line.</summary>
	public const int MaxExpressionLength = 65536;

	/// <summary><c>%0</c> to <c>%9</c>.</summary>
	public const int MaxArguments = 10;

	[HttpPost]
	public async Task<ActionResult<PortalCommandResponse>> Run([FromBody] PortalCommandRequest request, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(request.Command)) return BadRequest("A command is required.");
		if (request.Command.Length > MaxCommandLength || request.Result?.Length > MaxCommandLength)
			return BadRequest($"A command or result expression may be at most {MaxCommandLength} characters.");
		// One line: a newline would make the second line part of the first command's argument, not a command.
		if (request.Command.AsSpan().ContainsAny('\r', '\n')) return BadRequest("A command is one line.");

		DBRef? pinned = null;
		if (request.Character is not null && !DBRef.TryParse(request.Character, out pinned))
			return BadRequest("The character must be an objid.");

		if (User.GetCapabilityActor() is not { } actor
			|| await User.ResolvePlayerAsync(projection, ct) is not { } character) return Unauthorized();
		if (pinned is { } expected && !character.Object.DBRef.Matches(expected))
			return Problem($"This session now acts as {character.Object.Name}, not {expected}; the command was not run.",
				statusCode: StatusCodes.Status409Conflict);

		return Answer(await commands.RunAsync(actor.AccountId, character, request, ct));
	}

	[HttpPost("eval")]
	public async Task<ActionResult<PortalCommandResponse>> Evaluate([FromBody] PortalEvalRequest request, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(request.Expression)) return BadRequest("An expression is required.");
		if (request.Expression.Length > MaxExpressionLength)
			return BadRequest($"An expression may be at most {MaxExpressionLength} characters.");
		if (request.Arguments is { Count: > MaxArguments }) return BadRequest($"At most {MaxArguments} arguments, %0 to %9.");
		if (request.Arguments?.Any(a => a is null || a.Length > MaxCommandLength) == true)
			return BadRequest($"An argument may be at most {MaxCommandLength} characters.");

		DBRef? pinned = null;
		if (request.Character is not null && !DBRef.TryParse(request.Character, out pinned))
			return BadRequest("The character must be an objid.");

		if (User.GetCapabilityActor() is not { } actor
			|| await User.ResolvePlayerAsync(projection, ct) is not { } character) return Unauthorized();
		if (pinned is { } expected && !character.Object.DBRef.Matches(expected))
			return Problem($"This session now acts as {character.Object.Name}, not {expected}; nothing was evaluated.",
				statusCode: StatusCodes.Status409Conflict);

		DBRef? self = null;
		if (request.Object is { } number)
		{
			// Unknown and imperceptible read the same, as the object API answers them.
			if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), ct) is not AnySharpObject target
				|| !await reality.CanPerceiveAsync(character.Object.DBRef, target.Object().DBRef, ct))
				return NotFound();
			if (!await permissions.Controls(character, target))
				return Problem(ErrorMessages.Returns.PermissionDenied, statusCode: StatusCodes.Status403Forbidden);
			self = target.Object().DBRef;
		}

		return Answer(await commands.EvaluateAsync(actor.AccountId, character, self, request, ct));
	}

	private ActionResult<PortalCommandResponse> Answer(PortalCommandOutcome outcome) => outcome switch
	{
		PortalCommandResponse response => response,
		TooManyCommands busy => Problem($"At most {busy.Limit} commands may be pending at once; try again when one has finished.",
			statusCode: StatusCodes.Status429TooManyRequests),
		Error<string> error => Problem(error.Value, statusCode: StatusCodes.Status503ServiceUnavailable),
	};
}
