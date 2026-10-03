using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The portal's one route for game commands: runs a line as the account session's character and
/// answers with what it produced.
///
/// Routes:
///   POST /api/commands   — { command, result?, character? } → { output, result, truncated }
///
/// It acts as the character the session is bound to, whatever character a terminal in the same tab
/// is playing. A request that names a <c>character</c> runs only while the session is still bound to
/// it, and is answered 409 otherwise — so a flow of several commands cannot carry on as another
/// character when the tab switches mid-way. An account with too many commands pending is answered 429. It runs anything the character could type, softcode <c>$</c>-commands included, and
/// nothing more: the character's own permissions decide what the command may do.
/// </summary>
[ApiController]
[Route("api/commands")]
[Authorize]
public class CommandsController(IPortalCommandService commands, IVisibleWorldProjection projection) : ControllerBase
{
	/// <summary>The longest line accepted, PennMUSH's <c>BUFFER_LEN</c> for a typed command.</summary>
	public const int MaxCommandLength = 8192;

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

		return await commands.RunAsync(actor.AccountId, character, request, ct) switch
		{
			PortalCommandResponse response => response,
			TooManyCommands busy => Problem($"At most {busy.Limit} commands may be pending at once; try again when one has finished.",
				statusCode: StatusCodes.Status429TooManyRequests),
			Error<string> error => Problem(error.Value, statusCode: StatusCodes.Status503ServiceUnavailable),
		};
	}
}
