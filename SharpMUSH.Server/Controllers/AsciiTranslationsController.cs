using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The <c>ascii_translations</c> table: what a client without Unicode is sent for a character. One entry per
/// character; setting one again replaces its text. The same table <c>@ascii</c> edits in the game.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class AsciiTranslationsController(
	IOptionsWrapper<SharpMUSHOptions> options,
	IConfigOptionWriter config,
	IAuditLog audit)
	: ControllerBase
{
	[HttpGet]
	public ActionResult<Dictionary<string, string>> Get()
		=> Ok(options.CurrentValue.AsciiTranslations.Translations);

	[HttpPost]
	public async Task<ActionResult> Set([FromBody] AsciiTranslationRequest request)
	{
		var character = request.Character ?? string.Empty;
		var text = request.Text ?? string.Empty;
		if (AsciiTranslations.Problem(character, text) is { } problem)
		{
			return BadRequest(problem);
		}

		await config.UpdateAsync(current =>
			current.AsciiTranslations.Translations.TryGetValue(character, out var had) && had == text
				? current
				: current with
				{
					AsciiTranslations = new AsciiTranslationsOptions(
						new Dictionary<string, string>(current.AsciiTranslations.Translations) { [character] = text })
				});
		await audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, "ascii_translations"),
			$"{character}={text}");
		return Ok();
	}

	/// <remarks>The character comes in the query string: a route segment can't carry <c>/</c>, nor keep a lone combining mark intact.</remarks>
	[HttpDelete]
	public async Task<ActionResult> Delete([FromQuery] string character)
	{
		var removed = false;
		await config.UpdateAsync(current =>
		{
			var table = new Dictionary<string, string>(current.AsciiTranslations.Translations);
			removed = table.Remove(character);
			return removed ? current with { AsciiTranslations = new AsciiTranslationsOptions(table) } : current;
		});

		if (!removed)
		{
			return NotFound($"'{character}' has no translation");
		}

		await audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, "ascii_translations"),
			$"remove {character}");
		return Ok();
	}
}
