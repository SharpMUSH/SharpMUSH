using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController(
	IOptionsWrapper<SharpMUSHOptions> options,
	ISharpDatabase database,
	ConfigurationReloadService configReloadService,
	ILogger<ConfigurationController> logger)
	: ControllerBase
{
	[HttpGet]
	public ActionResult<ConfigurationResponse> GetConfiguration()
	{
		try
		{
			var configuration = options.CurrentValue;
			var converted = OptionHelper.OptionsToConfigurationResponse(configuration);

			return Ok(converted);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error retrieving configuration");
			return StatusCode(500, "Error retrieving configuration");
		}
	}

	[HttpPost("import")]
	public async Task<ActionResult<ConfigurationResponse>> ImportConfiguration([FromBody] string configContent)
	{
		try
		{
			// Create a temporary file with the content
			var tempFile = Path.GetTempFileName();
			await System.IO.File.WriteAllTextAsync(tempFile, configContent);

			// Use ReadPennMushConfig to parse it
			var importedOptions = ReadPennMushConfig.Create(tempFile);

			// Clean up temp file
			System.IO.File.Delete(tempFile);

			// Store the new configuration in the database
			// Pass the object directly - the database will handle serialization
			await database.SetExpandedServerData(nameof(SharpMUSHOptions), importedOptions);

			// Signal that configuration has changed using the proper Microsoft pattern
			// This notifies IOptionsMonitor consumers via change tokens
			configReloadService.SignalChange();

			logger.LogInformation("Configuration imported and persisted successfully");

			return Ok(OptionHelper.OptionsToConfigurationResponse(importedOptions));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error importing configuration");
			return BadRequest($"Error importing configuration: {ex.Message}");
		}
	}

	[HttpPut]
	public async Task<ActionResult<ConfigurationResponse>> UpdateConfiguration([FromBody] Dictionary<string, JsonElement> updates)
	{
		try
		{
			if (updates == null || updates.Count == 0)
				return BadRequest(new { error = "No updates provided" });

			// Get current config and serialize to JSON for patching
			var current = options.CurrentValue;
			var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
			var json = JsonSerializer.SerializeToNode(current, jsonOptions)!.AsObject();

			foreach (var (path, value) in updates)
			{
				var parts = path.Split('.');
				if (parts.Length != 2) continue;

				var categoryName = parts[0];
				var propertyName = parts[1];

				if (json[categoryName] is JsonObject category)
				{
					category[propertyName] = JsonNode.Parse(value.GetRawText());
				}
			}

			var updated = json.Deserialize<SharpMUSHOptions>(new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = false
			});

			if (updated == null)
				return BadRequest(new { error = "Failed to deserialize updated configuration" });

			// Validate using the generated validator
			var validator = new ValidateSharpOptions();
			var validationResult = validator.Validate(null, updated);
			if (validationResult.Failed)
				return BadRequest(new { error = validationResult.FailureMessage });

			// Persist to database
			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updated);

			// Signal reload to all IOptionsMonitor consumers
			configReloadService.SignalChange();

			logger.LogInformation("Configuration updated: {Count} properties changed", updates.Count);

			return Ok(OptionHelper.OptionsToConfigurationResponse(updated));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error updating configuration");
			return BadRequest(new { error = $"Error updating configuration: {ex.Message}" });
		}
	}
}