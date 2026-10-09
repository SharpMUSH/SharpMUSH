using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class ConfigurationController(
	IOptionsWrapper<SharpMUSHOptions> options,
	IConfigOptionWriter config,
	MushCnfImportService mushCnf,
	IAuditLog audit,
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

			var corrections = new List<ConfigBoundCorrection>();
			var errors = new Dictionary<string, string>();
			string? refused = null;
			var updated = await config.UpdateAsync(current =>
			{
				var changed = ApplyUpdates(current, updates, corrections, out errors);
				if (errors.Count > 0)
				{
					return current;
				}

				var validationResult = new ValidateSharpOptions().Validate(null, changed);
				refused = validationResult.Failed ? validationResult.FailureMessage : null;
				return refused is null ? changed : current;
			});

			if (errors.Count > 0)
			{
				return BadRequest(new { errors = errors });
			}

			if (refused is not null)
			{
				return BadRequest(new { errors = new Dictionary<string, string> { ["_global"] = refused } });
			}

			// Logged once stored: a correction in a refused update never took effect.
			foreach (var correction in corrections)
			{
				logger.LogWarning("Configuration value clamped to its declared range: {Correction}", correction.ToString());
			}

			logger.LogInformation("Configuration updated: {Properties}", string.Join(", ", updates.Keys));
			foreach (var (key, value) in updates)
			{
				await audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, key),
					value.GetRawText());
			}

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
	/// Applies partial updates to the immutable record hierarchy. Updates are keyed by property path,
	/// e.g. "Net.Port" or "Limit.MaxLogins"; both halves match case-insensitively. A number outside the
	/// option's declared range is clamped to the bound and added to <paramref name="corrections"/>, for the caller to log once stored (#1335).
	/// </summary>
	private static SharpMUSHOptions ApplyUpdates(
		SharpMUSHOptions current,
		Dictionary<string, JsonElement> updates,
		List<ConfigBoundCorrection> corrections,
		out Dictionary<string, string> errors)
	{
		errors = new Dictionary<string, string>();
		var result = current;

		foreach (var (path, value) in updates)
		{
			switch (ApplyUpdate(result, path, value, corrections))
			{
				case SharpMUSHOptions applied:
					result = applied;
					break;
				case Error<string> error:
					errors[path] = error.Value;
					break;
			}
		}

		return result;
	}

	private static Result<SharpMUSHOptions> ApplyUpdate(
		SharpMUSHOptions current,
		string path,
		JsonElement value,
		List<ConfigBoundCorrection> corrections) =>
		ResolvePropertyPath(path) switch
		{
			string property => ApplyValue(current, property, value, corrections),
			Error<string> error => error
		};

	private static Result<string> ResolvePropertyPath(string path)
	{
		var parts = path.Split('.', 2);
		if (parts.Length != 2)
		{
			return new Error<string>($"Invalid property path: '{path}'. Expected format: 'Category.Property'");
		}

		var category = ConfigAccessor.ResolveCategoryName(parts[0]);
		if (category is null)
		{
			return new Error<string>($"Unknown category: '{parts[0]}'");
		}

		// A property name alone identifies the property — they are unique across categories — so the
		// category half is checked rather than used, and a path naming a property that lives in another
		// category is rejected instead of being dropped on the floor.
		var property = ConfigAccessor.ResolvePropertyName(parts[1]);
		return property is null || ConfigAccessor.GetCategoryForProperty(property) != category
			? new Error<string>($"Unknown property: '{parts[1]}' in category '{category}'")
			: property;
	}

	private static Result<SharpMUSHOptions> ApplyValue(
		SharpMUSHOptions current,
		string property,
		JsonElement value,
		List<ConfigBoundCorrection> corrections)
	{
		try
		{
			return ConvertJsonElement(value, ConfigAccessor.GetPropertyType(property)!) switch
			{
				Error<string> error => new Error<string>($"Invalid value: {error.Value}"),
				None => ConfigAccessor.WithValue(current, property, null, corrections.Add),
				Converted converted => ConfigAccessor.WithValue(current, property, converted.Value, corrections.Add)
			};
		}
		// Only what reading a JsonElement and assigning it can raise. Anything else — a null
		// dereference, a missing switch arm in the generated setter — is a defect in this code, and
		// reporting it to the caller as "Invalid value" would bury it in a 400.
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException
			or OverflowException or InvalidCastException or ArgumentException or NotSupportedException)
		{
			return new Error<string>($"Invalid value: {ex.Message}");
		}
	}

	private readonly record struct Converted(object Value);

	/// <summary>The value an option is set to, null (<see cref="None"/>), or why the JSON cannot be one.</summary>
	/// <remarks>The value is wrapped: an <c>object</c> case would also match the union itself in a switch.</remarks>
	private union ConvertedValue(Converted, None, Error<string>);

	private static ConvertedValue ConvertJsonElement(JsonElement element, Type targetType)
	{
		var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		return actualType switch
		{
			_ when element.ValueKind == JsonValueKind.Null => NullFor(targetType),
			_ when actualType == typeof(uint) => ReadUInt32(element),
			_ when actualType == typeof(char) => ReadChar(element),
			_ => ReadValue(element, actualType, targetType) is { } value ? new Converted(value) : new None()
		};
	}

	private static ConvertedValue NullFor(Type targetType) =>
		Nullable.GetUnderlyingType(targetType) is not null || !targetType.IsValueType
			? new None()
			: new Error<string>($"Cannot set non-nullable type {targetType.Name} to null");

	// Handle negative values sent as int
	private static ConvertedValue ReadUInt32(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Number when element.TryGetUInt32(out var uval) => new Converted(uval),
		JsonValueKind.Number when element.TryGetInt32(out var ival) && ival >= 0 => new Converted((uint)ival),
		JsonValueKind.Number => new Error<string>($"Value {element} is out of range for uint"),
		_ => new Error<string>($"Expected number, got {element.ValueKind}")
	};

	private static ConvertedValue ReadChar(JsonElement element) =>
		element.GetString() is { Length: > 0 } str
			? new Converted(str[0])
			: new Error<string>("Empty string for char");

	private static object? ReadValue(JsonElement element, Type actualType, Type targetType) => actualType switch
	{
		_ when actualType == typeof(bool) => element.GetBoolean(),
		_ when actualType == typeof(int) => element.GetInt32(),
		_ when actualType == typeof(long) => element.GetInt64(),
		_ when actualType == typeof(double) => element.GetDouble(),
		_ when actualType == typeof(float) => element.GetSingle(),
		_ when actualType == typeof(string) => element.GetString(),
		_ when actualType == typeof(string[]) => ReadStrings(element),
		_ when actualType == typeof(Dictionary<string, string[]>) => ReadStringArrays(element),
		_ => JsonSerializer.Deserialize(element.GetRawText(), targetType)
	};

	private static string[] ReadStrings(JsonElement element) =>
		element.EnumerateArray().Select(e => e.GetString()!).ToArray();

	// The indexer rather than ToDictionary: a key repeated in the JSON keeps its last value instead of
	// failing the update.
	private static Dictionary<string, string[]> ReadStringArrays(JsonElement element)
	{
		var dict = new Dictionary<string, string[]>();
		foreach (var prop in element.EnumerateObject())
		{
			dict[prop.Name] = ReadStrings(prop.Value);
		}

		return dict;
	}

	[HttpPost("import")]
	public async Task<ActionResult<ConfigurationResponse>> ImportConfiguration([FromBody] string configContent)
	{
		try
		{
			var importedOptions = await mushCnf.ApplyAsync(await mushCnf.ReadAsync(configContent));
			await audit.RecordPortalAsync(User, AuditActions.ConfigImport, null, "mush.cnf");
			return Ok(OptionHelper.OptionsToConfigurationResponse(importedOptions));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error importing configuration");
			return BadRequest($"Error importing configuration: {ex.Message}");
		}
	}
}