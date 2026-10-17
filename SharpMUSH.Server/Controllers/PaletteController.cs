using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The command palette's server side, for the account session's bound character.
///
/// Routes:
///   GET /api/palette?input=&lt;session id&gt; — the entries, built-ins, pins and context stack
///   PUT /api/palette/pins                 — { pins } → 204; writes the character's PALETTE_PINS
///
/// Entries are filtered for the character (scopes, perception, locks); running one goes through the
/// terminal or <c>POST api/commands</c>, which check everything again.
/// </summary>
[ApiController]
[Route("api/palette")]
[Authorize]
public class PaletteController(
	IPaletteService palette,
	IAttributeService attributes,
	IVisibleWorldProjection projection) : ControllerBase
{
	/// <summary>The most pins a character keeps.</summary>
	public const int MaxPins = 50;

	/// <summary>The longest pin id: an objid and an attribute name, or a client id.</summary>
	public const int MaxPinLength = PalettePinsRequest.MaxPinLength;

	[HttpGet]
	public async Task<ActionResult<PaletteResponse>> Get([FromQuery] Guid? input, CancellationToken ct)
	{
		if (await User.ResolvePlayerAsync(projection, ct) is not { } character) return Unauthorized();
		return await palette.ResolveAsync(character, input, ct);
	}

	[HttpPut("pins")]
	public async Task<IActionResult> SetPins([FromBody] PalettePinsRequest request, CancellationToken ct)
	{
		var pins = request.Pins ?? [];
		if (pins.Count > MaxPins) return BadRequest($"At most {MaxPins} pins.");
		if (pins.Any(pin => !PalettePinsRequest.IsValidId(pin)))
			return BadRequest($"A pin is one id of at most {MaxPinLength} characters, without spaces.");
		if (await User.ResolvePlayerAsync(projection, ct) is not { } character) return Unauthorized();

		var written = pins.Count == 0
			? await attributes.ClearAttributeAsync(character, character, PaletteEntry.PinsAttribute,
				IAttributeService.AttributePatternMode.Exact)
			: await attributes.SetAttributeAsync(character, character, PaletteEntry.PinsAttribute,
				MString.Plain(string.Join(' ', pins.Distinct(StringComparer.Ordinal))));

		return written switch
		{
			Success => NoContent(),
			// Clearing pins that were never set is not a failure.
			Error<string> when pins.Count == 0 => NoContent(),
			Error<string> error => Problem(error.Value, statusCode: StatusCodes.Status403Forbidden)
		};
	}
}
