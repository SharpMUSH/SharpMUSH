using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
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

	[HttpGet("export")]
	public ActionResult ExportConfiguration()
	{
		try
		{
			var configuration = options.CurrentValue;
			var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
			return Content(json, "application/json");
		}
		catch (JsonException ex)
		{
			logger.LogError(ex, "Error exporting configuration");
			return StatusCode(500, "Error exporting configuration");
		}
	}

	[HttpPatch]
	public async Task<ActionResult<ConfigurationResponse>> UpdateConfiguration(
		[FromBody] Dictionary<string, JsonElement> updates)
	{
		try
		{
			if (updates.Count == 0)
			{
				return BadRequest(new { errors = "No updates provided" });
			}

			var current = options.CurrentValue;
			var updated = ApplyUpdates(current, updates, out var errors);

			if (errors.Count > 0)
			{
				return BadRequest(new { errors = errors });
			}

			var validator = new ValidateSharpOptions();
			var validationResult = validator.Validate(null, updated);
			if (validationResult.Failed)
			{
				return BadRequest(new { errors = new Dictionary<string, string> { ["_global"] = validationResult.FailureMessage } });
			}

			await database.SetExpandedServerData(nameof(SharpMUSHOptions), updated);
			configReloadService.SignalChange();

			logger.LogInformation("Configuration updated: {Properties}", string.Join(", ", updates.Keys));

			return Ok(OptionHelper.OptionsToConfigurationResponse(updated));
		}
		catch (JsonException ex)
		{
			logger.LogError(ex, "Error updating configuration");
			return BadRequest(new { errors = new Dictionary<string, string> { ["_global"] = ex.Message } });
		}
		catch (InvalidOperationException ex)
		{
			logger.LogError(ex, "Error updating configuration");
			return BadRequest(new { errors = new Dictionary<string, string> { ["_global"] = ex.Message } });
		}
	}

	/// <summary>
	/// Apply partial updates to the immutable record hierarchy.
	/// Updates are keyed by property path, e.g. "Net.Port" or "Limit.MaxLogins".
	/// </summary>
	private static SharpMUSHOptions ApplyUpdates(
		SharpMUSHOptions current,
		Dictionary<string, JsonElement> updates,
		out Dictionary<string, string> errors)
	{
		errors = new Dictionary<string, string>();

		var grouped = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);
		foreach (var (path, value) in updates)
		{
			var parts = path.Split('.', 2);
			if (parts.Length != 2)
			{
				errors[path] = $"Invalid property path: '{path}'. Expected format: 'Category.Property'";
				continue;
			}

			if (!grouped.ContainsKey(parts[0]))
				grouped[parts[0]] = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

			grouped[parts[0]][parts[1]] = value;
		}

		var optionsType = typeof(SharpMUSHOptions);
		var result = current;

		foreach (var (categoryName, categoryUpdates) in grouped)
		{
			var categoryProp = optionsType.GetProperty(categoryName);
			if (categoryProp == null)
			{
				foreach (var key in categoryUpdates.Keys)
					errors[$"{categoryName}.{key}"] = $"Unknown category: '{categoryName}'";
				continue;
			}

			var categoryValue = categoryProp.GetValue(current);
			if (categoryValue == null) continue;

			var categoryType = categoryProp.PropertyType;
			var updatedCategory = ApplyCategoryUpdates(categoryValue, categoryType, categoryName, categoryUpdates, errors);

			result = CloneRecordWithProperty(result, categoryProp, updatedCategory);
		}

		return result;
	}

	private static object ApplyCategoryUpdates(
		object categoryValue,
		Type categoryType,
		string categoryName,
		Dictionary<string, JsonElement> updates,
		Dictionary<string, string> errors)
	{
		var ctor = categoryType.GetConstructors()
			.OrderByDescending(c => c.GetParameters().Length)
			.First();

		var ctorParams = ctor.GetParameters();
		var args = new object?[ctorParams.Length];

		for (var i = 0; i < ctorParams.Length; i++)
		{
			var param = ctorParams[i];
			var prop = categoryType.GetProperty(param.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			var currentVal = prop?.GetValue(categoryValue);

			if (updates.TryGetValue(param.Name!, out var jsonVal))
			{
				try
				{
					args[i] = ConvertJsonElement(jsonVal, param.ParameterType);
				}
				catch (Exception ex)
				{
					errors[$"{categoryName}.{param.Name}"] = $"Invalid value: {ex.Message}";
					args[i] = currentVal;
				}
			}
			else
			{
				args[i] = currentVal;
			}
		}

		return ctor.Invoke(args);
	}

	private static object? ConvertJsonElement(JsonElement element, Type targetType)
	{
		var underlyingType = Nullable.GetUnderlyingType(targetType);
		var isNullable = underlyingType != null;
		var actualType = underlyingType ?? targetType;

		if (element.ValueKind == JsonValueKind.Null)
		{
			if (isNullable || !targetType.IsValueType)
				return null;
			throw new InvalidOperationException($"Cannot set non-nullable type {targetType.Name} to null");
		}

		if (actualType == typeof(bool))
			return element.GetBoolean();

		if (actualType == typeof(int))
			return element.GetInt32();

		if (actualType == typeof(uint))
		{
			// Handle negative values sent as int
			if (element.ValueKind == JsonValueKind.Number)
			{
				if (element.TryGetUInt32(out var uval)) return uval;
				if (element.TryGetInt32(out var ival) && ival >= 0) return (uint)ival;
				throw new InvalidOperationException($"Value {element} is out of range for uint");
			}
			throw new InvalidOperationException($"Expected number, got {element.ValueKind}");
		}

		if (actualType == typeof(long))
			return element.GetInt64();

		if (actualType == typeof(double))
			return element.GetDouble();

		if (actualType == typeof(float))
			return element.GetSingle();

		if (actualType == typeof(string))
			return element.GetString();

		if (actualType == typeof(char))
		{
			var str = element.GetString();
			return str?.Length > 0 ? str[0] : throw new InvalidOperationException("Empty string for char");
		}

		if (actualType == typeof(string[]))
		{
			return element.EnumerateArray().Select(e => e.GetString()!).ToArray();
		}

		if (actualType == typeof(Dictionary<string, string[]>))
		{
			var dict = new Dictionary<string, string[]>();
			foreach (var prop in element.EnumerateObject())
			{
				dict[prop.Name] = prop.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
			}
			return dict;
		}

		return JsonSerializer.Deserialize(element.GetRawText(), targetType);
	}

	private static SharpMUSHOptions CloneRecordWithProperty(SharpMUSHOptions source, PropertyInfo prop, object? newValue)
	{
		// SharpMUSHOptions uses { get; init; } properties.
		// Build a new instance by copying all properties, overriding the target one.
		return new SharpMUSHOptions
		{
			Attribute = prop.Name == nameof(SharpMUSHOptions.Attribute) ? (AttributeOptions)newValue! : source.Attribute,
			Chat = prop.Name == nameof(SharpMUSHOptions.Chat) ? (ChatOptions)newValue! : source.Chat,
			Command = prop.Name == nameof(SharpMUSHOptions.Command) ? (CommandOptions)newValue! : source.Command,
			Compatibility = prop.Name == nameof(SharpMUSHOptions.Compatibility) ? (CompatibilityOptions)newValue! : source.Compatibility,
			Cosmetic = prop.Name == nameof(SharpMUSHOptions.Cosmetic) ? (CosmeticOptions)newValue! : source.Cosmetic,
			Cost = prop.Name == nameof(SharpMUSHOptions.Cost) ? (CostOptions)newValue! : source.Cost,
			Database = prop.Name == nameof(SharpMUSHOptions.Database) ? (DatabaseOptions)newValue! : source.Database,
			Dump = prop.Name == nameof(SharpMUSHOptions.Dump) ? (DumpOptions)newValue! : source.Dump,
			File = prop.Name == nameof(SharpMUSHOptions.File) ? (Configuration.Options.FileOptions)newValue! : source.File,
			Flag = prop.Name == nameof(SharpMUSHOptions.Flag) ? (FlagOptions)newValue! : source.Flag,
			Function = prop.Name == nameof(SharpMUSHOptions.Function) ? (FunctionOptions)newValue! : source.Function,
			Limit = prop.Name == nameof(SharpMUSHOptions.Limit) ? (LimitOptions)newValue! : source.Limit,
			Log = prop.Name == nameof(SharpMUSHOptions.Log) ? (LogOptions)newValue! : source.Log,
			Message = prop.Name == nameof(SharpMUSHOptions.Message) ? (MessageOptions)newValue! : source.Message,
			Net = prop.Name == nameof(SharpMUSHOptions.Net) ? (NetOptions)newValue! : source.Net,
			Debug = prop.Name == nameof(SharpMUSHOptions.Debug) ? (DebugOptions)newValue! : source.Debug,
			Alias = prop.Name == nameof(SharpMUSHOptions.Alias) ? (AliasOptions)newValue! : source.Alias,
			Restriction = prop.Name == nameof(SharpMUSHOptions.Restriction) ? (RestrictionOptions)newValue! : source.Restriction,
			BannedNames = prop.Name == nameof(SharpMUSHOptions.BannedNames) ? (BannedNamesOptions)newValue! : source.BannedNames,
			SitelockRules = prop.Name == nameof(SharpMUSHOptions.SitelockRules) ? (SitelockRulesOptions)newValue! : source.SitelockRules,
			Warning = prop.Name == nameof(SharpMUSHOptions.Warning) ? (WarningOptions)newValue! : source.Warning,
			TextFile = prop.Name == nameof(SharpMUSHOptions.TextFile) ? (TextFileOptions)newValue! : source.TextFile
		};
	}

	[HttpPost("import")]
	public async Task<ActionResult<ConfigurationResponse>> ImportConfiguration([FromBody] string configContent)
	{
		try
		{
			var tempFile = Path.GetTempFileName();
			await System.IO.File.WriteAllTextAsync(tempFile, configContent);

			var importedOptions = ReadPennMushConfig.Create(tempFile);

			System.IO.File.Delete(tempFile);

			// Pass the object directly - the database will handle serialization
			await database.SetExpandedServerData(nameof(SharpMUSHOptions), importedOptions);

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
}