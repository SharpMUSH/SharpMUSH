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
public class RestrictionsController(
	IOptionsWrapper<SharpMUSHOptions> options,
	IConfigOptionWriter config,
	IAuditLog audit,
	ILogger<RestrictionsController> logger)
	: ControllerBase
{
	#region Command Restrictions

	[HttpGet("commands")]
	public ActionResult<Dictionary<string, string[]>> GetCommandRestrictions()
	{
		try
		{
			var restrictions = options.CurrentValue.Restriction.CommandRestrictions;
			return Ok(restrictions);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving command restrictions");
			return StatusCode(500, "Error retrieving command restrictions");
		}
	}

	[HttpPost("commands/{commandName}")]
	public async Task<ActionResult> AddCommandRestriction(string commandName, [FromBody] string[] restrictions)
	{
		try
		{
			await config.UpdateAsync(current => current with
			{
				Restriction = current.Restriction with
				{
					CommandRestrictions = new Dictionary<string, string[]>(current.Restriction.CommandRestrictions)
					{
						[commandName] = restrictions
					}
				}
			});
			await audit.RecordPortalAsync(User, AuditActions.RestrictionSet, AuditTargets.Of(AuditTargetKinds.Command, commandName), string.Join(" ", restrictions));

			logger.LogInformation("Added/updated command restriction for {CommandName}", LogSanitizer.Sanitize(commandName));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding command restriction for {CommandName}", LogSanitizer.Sanitize(commandName));
			return StatusCode(500, "Error adding command restriction");
		}
	}

	[HttpDelete("commands/{commandName}")]
	public async Task<ActionResult> DeleteCommandRestriction(string commandName)
	{
		try
		{
			var removed = false;
			await config.UpdateAsync(current =>
			{
				var restrictions = new Dictionary<string, string[]>(current.Restriction.CommandRestrictions);
				removed = restrictions.Remove(commandName);
				return removed
					? current with { Restriction = current.Restriction with { CommandRestrictions = restrictions } }
					: current;
			});

			if (!removed)
			{
				return NotFound($"Command restriction '{commandName}' not found");
			}

			await audit.RecordPortalAsync(User, AuditActions.RestrictionClear, AuditTargets.Of(AuditTargetKinds.Command, commandName));

			logger.LogInformation("Deleted command restriction for {CommandName}", LogSanitizer.Sanitize(commandName));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting command restriction for {CommandName}", LogSanitizer.Sanitize(commandName));
			return StatusCode(500, "Error deleting command restriction");
		}
	}

	#endregion

	#region Function Restrictions

	[HttpGet("functions")]
	public ActionResult<Dictionary<string, string[]>> GetFunctionRestrictions()
	{
		try
		{
			var restrictions = options.CurrentValue.Restriction.FunctionRestrictions;
			return Ok(restrictions);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving function restrictions");
			return StatusCode(500, "Error retrieving function restrictions");
		}
	}

	[HttpPost("functions/{functionName}")]
	public async Task<ActionResult> AddFunctionRestriction(string functionName, [FromBody] string[] restrictions)
	{
		try
		{
			await config.UpdateAsync(current => current with
			{
				Restriction = current.Restriction with
				{
					FunctionRestrictions = new Dictionary<string, string[]>(current.Restriction.FunctionRestrictions)
					{
						[functionName] = restrictions
					}
				}
			});
			await audit.RecordPortalAsync(User, AuditActions.RestrictionSet, AuditTargets.Of(AuditTargetKinds.Function, functionName), string.Join(" ", restrictions));

			logger.LogInformation("Added/updated function restriction for {FunctionName}", LogSanitizer.Sanitize(functionName));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding function restriction for {FunctionName}", LogSanitizer.Sanitize(functionName));
			return StatusCode(500, "Error adding function restriction");
		}
	}

	[HttpDelete("functions/{functionName}")]
	public async Task<ActionResult> DeleteFunctionRestriction(string functionName)
	{
		try
		{
			var removed = false;
			await config.UpdateAsync(current =>
			{
				var restrictions = new Dictionary<string, string[]>(current.Restriction.FunctionRestrictions);
				removed = restrictions.Remove(functionName);
				return removed
					? current with { Restriction = current.Restriction with { FunctionRestrictions = restrictions } }
					: current;
			});

			if (!removed)
			{
				return NotFound($"Function restriction '{functionName}' not found");
			}

			await audit.RecordPortalAsync(User, AuditActions.RestrictionClear, AuditTargets.Of(AuditTargetKinds.Function, functionName));

			logger.LogInformation("Deleted function restriction for {FunctionName}", LogSanitizer.Sanitize(functionName));
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting function restriction for {FunctionName}", LogSanitizer.Sanitize(functionName));
			return StatusCode(500, "Error deleting function restriction");
		}
	}

	#endregion
}
