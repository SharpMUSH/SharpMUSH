using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Helpers;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SitelockController(
	IOptionsWrapper<SharpMUSHOptions> options,
	ISharpDatabase database,
	ConfigurationReloadService configReloadService,
	ILogger<SitelockController> logger)
	: ControllerBase
{
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

			var currentOptions = options.CurrentValue;
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
			var currentOptions = options.CurrentValue;
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
