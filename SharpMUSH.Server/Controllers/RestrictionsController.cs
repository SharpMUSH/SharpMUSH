using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RestrictionsController(
	IOptionsWrapper<SharpMUSHOptions> options,
	ISharpDatabase database,
	ConfigurationReloadService configReloadService,
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
			var currentOptions = options.CurrentValue;
			var newRestrictions = new Dictionary<string, string[]>(currentOptions.Restriction.CommandRestrictions)
			{
				[commandName] = restrictions
			};

			var updatedOptions = currentOptions with
			{
				Restriction = currentOptions.Restriction with
				{
					CommandRestrictions = newRestrictions
				}
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();

			logger.LogInformation("Added/updated command restriction for {CommandName}", commandName);
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding command restriction for {CommandName}", commandName);
			return StatusCode(500, $"Error adding command restriction: {ex.Message}");
		}
	}

	[HttpDelete("commands/{commandName}")]
	public async Task<ActionResult> DeleteCommandRestriction(string commandName)
	{
		try
		{
			var currentOptions = options.CurrentValue;
			var newRestrictions = new Dictionary<string, string[]>(currentOptions.Restriction.CommandRestrictions);
			
			if (!newRestrictions.Remove(commandName))
			{
				return NotFound($"Command restriction '{commandName}' not found");
			}

			var updatedOptions = currentOptions with
			{
				Restriction = currentOptions.Restriction with
				{
					CommandRestrictions = newRestrictions
				}
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();

			logger.LogInformation("Deleted command restriction for {CommandName}", commandName);
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting command restriction for {CommandName}", commandName);
			return StatusCode(500, $"Error deleting command restriction: {ex.Message}");
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
			var currentOptions = options.CurrentValue;
			var newRestrictions = new Dictionary<string, string[]>(currentOptions.Restriction.FunctionRestrictions)
			{
				[functionName] = restrictions
			};

			var updatedOptions = currentOptions with
			{
				Restriction = currentOptions.Restriction with
				{
					FunctionRestrictions = newRestrictions
				}
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();

			logger.LogInformation("Added/updated function restriction for {FunctionName}", functionName);
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding function restriction for {FunctionName}", functionName);
			return StatusCode(500, $"Error adding function restriction: {ex.Message}");
		}
	}

	[HttpDelete("functions/{functionName}")]
	public async Task<ActionResult> DeleteFunctionRestriction(string functionName)
	{
		try
		{
			var currentOptions = options.CurrentValue;
			var newRestrictions = new Dictionary<string, string[]>(currentOptions.Restriction.FunctionRestrictions);
			
			if (!newRestrictions.Remove(functionName))
			{
				return NotFound($"Function restriction '{functionName}' not found");
			}

			var updatedOptions = currentOptions with
			{
				Restriction = currentOptions.Restriction with
				{
					FunctionRestrictions = newRestrictions
				}
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();

			logger.LogInformation("Deleted function restriction for {FunctionName}", functionName);
			return Ok();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error deleting function restriction for {FunctionName}", functionName);
			return StatusCode(500, $"Error deleting function restriction: {ex.Message}");
		}
	}

	#endregion
}
