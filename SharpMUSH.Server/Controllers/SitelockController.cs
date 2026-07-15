using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Helpers;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class SitelockController(
	IOptionsWrapper<SharpMUSHOptions> options,
	ISharpDatabase database,
	ConfigurationReloadService configReloadService,
	IBanEnforcer banEnforcer,
	ILogger<SitelockController> logger)
	: ControllerBase
{
	/// <summary>
	/// The freshest known <see cref="SharpMUSHOptions"/>: whatever is actually persisted in the
	/// database, falling back to <see cref="options"/>'s in-memory snapshot only if nothing has
	/// been persisted yet. Read-modify-write mutations (<see cref="AddSitelockRule"/>,
	/// <see cref="DeleteSitelockRule"/>) must base their merge on this rather than on
	/// <c>options.CurrentValue</c> alone: <c>IOptionsWrapper&lt;SharpMUSHOptions&gt;</c> re-reads
	/// lazily off the reload change-token, and a rule persisted moments earlier by a *different*
	/// mutation (e.g. via the in-game <c>@sitelock</c> command, or a prior request) could otherwise
	/// be silently dropped by an overwrite based on a stale in-memory copy. Mirrors
	/// <c>WizardCommands.CurrentPersistedOptionsAsync</c> (SharpMUSH.Implementation).
	/// </summary>
	private async ValueTask<SharpMUSHOptions> CurrentPersistedOptionsAsync()
		=> await database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions))
			?? options.CurrentValue;

	[HttpGet]
	public ActionResult<Dictionary<string, string[]>> GetSitelockRules()
	{
		try
		{
			var rules = options.CurrentValue.SitelockRules.Rules;
			return Ok(rules);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving sitelock rules");
			return StatusCode(500, "Error retrieving sitelock rules");
		}
	}

	[HttpPost("{hostPattern}")]
	public async Task<ActionResult> AddSitelockRule(string hostPattern, [FromBody] string[] accessRules)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(hostPattern))
			{
				return BadRequest("Host pattern cannot be empty");
			}

			if (accessRules == null || accessRules.Length == 0)
			{
				return BadRequest("At least one access rule is required");
			}

			var currentOptions = await CurrentPersistedOptionsAsync();
			var newRules = new Dictionary<string, string[]>(currentOptions.SitelockRules.Rules)
			{
				[hostPattern] = accessRules
			};

			var updatedOptions = currentOptions with
			{
				SitelockRules = new SitelockRulesOptions(newRules)
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();
			await banEnforcer.EnforceHostRuleAsync(hostPattern);

			logger.LogInformation("Added/updated sitelock rule for {HostPattern}", LogSanitizer.Sanitize(hostPattern));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding sitelock rule for {HostPattern}", LogSanitizer.Sanitize(hostPattern));
			return StatusCode(500, "Error adding sitelock rule");
		}
	}

	[HttpDelete("{hostPattern}")]
	public async Task<ActionResult> DeleteSitelockRule(string hostPattern)
	{
		try
		{
			var currentOptions = await CurrentPersistedOptionsAsync();
			var newRules = new Dictionary<string, string[]>(currentOptions.SitelockRules.Rules);

			if (!newRules.Remove(hostPattern))
			{
				return NotFound($"Sitelock rule for '{hostPattern}' not found");
			}

			var updatedOptions = currentOptions with
			{
				SitelockRules = new SitelockRulesOptions(newRules)
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();

			logger.LogInformation("Deleted sitelock rule for {HostPattern}", LogSanitizer.Sanitize(hostPattern));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting sitelock rule for {HostPattern}", LogSanitizer.Sanitize(hostPattern));
			return StatusCode(500, "Error deleting sitelock rule");
		}
	}
}
