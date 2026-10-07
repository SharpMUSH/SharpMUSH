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
public class BannedNamesController(
	IOptionsWrapper<SharpMUSHOptions> options,
	IConfigOptionWriter config,
	IAuditLog audit,
	ILogger<BannedNamesController> logger)
	: ControllerBase
{
	[HttpGet]
	public ActionResult<string[]> GetBannedNames()
	{
		try
		{
			var bannedNames = options.CurrentValue.BannedNames.BannedNames;
			return Ok(bannedNames);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving banned names");
			return StatusCode(500, "Error retrieving banned names");
		}
	}

	[HttpPost]
	public async Task<ActionResult> AddBannedName([FromBody] string name)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return BadRequest("Name cannot be empty");
			}

			var added = false;
			await config.UpdateAsync(current =>
			{
				var names = current.BannedNames.BannedNames;
				added = !names.Contains(name, StringComparer.OrdinalIgnoreCase);
				return added ? current with { BannedNames = new BannedNamesOptions([.. names, name]) } : current;
			});

			if (!added)
			{
				return Conflict($"Name '{name}' is already banned");
			}

			await audit.RecordPortalAsync(User, AuditActions.BannedNameAdd, AuditTargets.Of(AuditTargetKinds.Name, name));

			logger.LogInformation("Added banned name: {Name}", LogSanitizer.Sanitize(name));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding banned name: {Name}", LogSanitizer.Sanitize(name));
			return StatusCode(500, "Error adding banned name");
		}
	}

	[HttpDelete("{name}")]
	public async Task<ActionResult> DeleteBannedName(string name)
	{
		try
		{
			var removed = false;
			await config.UpdateAsync(current =>
			{
				var names = current.BannedNames.BannedNames.ToList();
				removed = names.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
				return removed ? current with { BannedNames = new BannedNamesOptions([.. names]) } : current;
			});

			if (!removed)
			{
				return NotFound($"Banned name '{name}' not found");
			}

			await audit.RecordPortalAsync(User, AuditActions.BannedNameRemove, AuditTargets.Of(AuditTargetKinds.Name, name));

			logger.LogInformation("Deleted banned name: {Name}", LogSanitizer.Sanitize(name));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting banned name: {Name}", LogSanitizer.Sanitize(name));
			return StatusCode(500, "Error deleting banned name");
		}
	}
}
