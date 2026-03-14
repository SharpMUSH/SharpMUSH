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
public class BannedNamesController(
	IOptionsWrapper<SharpMUSHOptions> options,
	ISharpDatabase database,
	ConfigurationReloadService configReloadService,
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

			var currentOptions = options.CurrentValue;
			var currentNames = currentOptions.BannedNames.BannedNames.ToList();

			if (currentNames.Contains(name, StringComparer.OrdinalIgnoreCase))
			{
				return Conflict($"Name '{name}' is already banned");
			}

			currentNames.Add(name);

			var updatedOptions = currentOptions with
			{
				BannedNames = new BannedNamesOptions(currentNames.ToArray())
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();

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
			var currentOptions = options.CurrentValue;
			var currentNames = currentOptions.BannedNames.BannedNames.ToList();

			var removed = currentNames.RemoveAll(n =>
				n.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;

			if (!removed)
			{
				return NotFound($"Banned name '{name}' not found");
			}

			var updatedOptions = currentOptions with
			{
				BannedNames = new BannedNamesOptions(currentNames.ToArray())
			};

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updatedOptions);
			configReloadService.SignalChange();

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
