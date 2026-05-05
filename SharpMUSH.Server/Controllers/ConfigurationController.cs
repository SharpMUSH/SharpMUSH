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

			// Get current config
			var current = options.CurrentValue;

			// Apply updates via reflection on the live object
			var updated = ApplyUpdatesViaReflection(current, updates);

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

	/// <summary>
	/// Applies updates to a SharpMUSHOptions record by patching one category at a time.
	/// Uses the record clone constructor (via reflection) to produce a new SharpMUSHOptions
	/// with the updated category, avoiding full deserialization of the top-level record.
	/// </summary>
	private static SharpMUSHOptions ApplyUpdatesViaReflection(SharpMUSHOptions current, Dictionary<string, JsonElement> updates)
	{
		// Group updates by category
		var grouped = updates
			.Where(kv => kv.Key.Contains('.'))
			.GroupBy(kv => kv.Key.Split('.')[0]);

		var result = current;
		var optionsType = typeof(SharpMUSHOptions);

		foreach (var group in grouped)
		{
			var categoryName = group.Key;
			var categoryProp = optionsType.GetProperty(categoryName);
			if (categoryProp == null) continue;

			var categoryValue = categoryProp.GetValue(result);
			if (categoryValue == null) continue;

			// Serialize just this category to JSON, patch it, deserialize back
			var categoryJson = JsonSerializer.SerializeToNode(categoryValue)!.AsObject();

			foreach (var (path, value) in group)
			{
				var propertyName = path.Split('.')[1];
				categoryJson[propertyName] = JsonNode.Parse(value.GetRawText());
			}

			var updatedCategory = categoryJson.Deserialize(categoryProp.PropertyType, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});

			// Use the record copy constructor + init setter via reflection
			// Records generate a <Clone>$ method we can use
			var cloneMethod = optionsType.GetMethod("<Clone>$")
				?? throw new InvalidOperationException($"Could not find <Clone>$ method on {optionsType.Name}. Ensure it is a record type.");
			var cloned = cloneMethod.Invoke(result, null)
				?? throw new InvalidOperationException("Clone returned null");

			// Init-only setters are still settable via reflection
			categoryProp.SetValue(cloned, updatedCategory);
			result = (SharpMUSHOptions)cloned;

			// Verify the update took effect
			if (categoryProp.GetValue(result) == null)
				throw new InvalidOperationException($"Failed to set {categoryName} on cloned record");
		}

		return result;
	}
}