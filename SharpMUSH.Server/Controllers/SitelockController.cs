using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class SitelockController(
	IOptionsWrapper<SharpMUSHOptions> options,
	IConfigOptionWriter config,
	IBanEnforcer banEnforcer,
	IAuditLog audit,
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

			await config.UpdateAsync(current => current with
			{
				SitelockRules = new SitelockRulesOptions(new Dictionary<string, string[]>(current.SitelockRules.Rules)
				{
					[hostPattern] = accessRules
				})
			});
			await audit.RecordPortalAsync(User, AuditActions.SitelockAdd, AuditTargets.Of(AuditTargetKinds.Host, hostPattern),
				string.Join(" ", accessRules));
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
			var removed = false;
			await config.UpdateAsync(current =>
			{
				var rules = new Dictionary<string, string[]>(current.SitelockRules.Rules);
				removed = rules.Remove(hostPattern);
				return removed ? current with { SitelockRules = new SitelockRulesOptions(rules) } : current;
			});

			if (!removed)
			{
				return NotFound($"Sitelock rule for '{hostPattern}' not found");
			}

			await audit.RecordPortalAsync(User, AuditActions.SitelockRemove, AuditTargets.Of(AuditTargetKinds.Host, hostPattern));

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
