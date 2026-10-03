using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The portal's one route for game commands: runs a line as the account session's character and
/// answers with what it produced.
///
/// Routes:
///   POST /api/commands   — { command, result? } → { output, result, truncated }
///
/// It acts as the character the session is bound to, whatever character a terminal in the same tab
/// is playing. It runs anything the character could type, softcode <c>$</c>-commands included, and
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

		if (await User.ResolvePlayerAsync(projection, ct) is not { } character) return Unauthorized();

		return await commands.RunAsync(character, request, ct) switch
		{
			PortalCommandResponse response => response,
			Error<string> error => Problem(error.Value, statusCode: StatusCodes.Status503ServiceUnavailable),
		};
	}
}
