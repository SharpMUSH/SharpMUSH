using System.Text.Json;

namespace SharpMUSH.Library.Applications;

/// <summary>A versioned plain UI definition. Config schemas deliberately support a small documented subset.</summary>
public sealed record UiDefinition(int Version, string Id, string Entry, int ConfigVersion, JsonElement ConfigSchema)
{
	public const string FileName = "ui.json";
	public const int MaxConfigBytes = 32 * 1024;
	public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public static bool IsName(string? value) => value is { Length: > 0 and <= 100 }
		&& value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && !value.Contains("..", StringComparison.Ordinal)
		&& value[0] != '.';

	public static string? ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
	{
		".html" => "text/html; charset=utf-8",
		".js" => "text/javascript; charset=utf-8",
		".css" => "text/css; charset=utf-8",
		".json" => "application/json",
		".png" => "image/png",
		".svg" => "image/svg+xml",
		_ => null
	};

	public static UiDefinition Parse(byte[] bytes)
	{
		var definition = JsonSerializer.Deserialize<UiDefinition>(bytes, JsonOptions)
			?? throw new FormatException("ui.json is empty.");
		if (definition.Version != 1 || definition.ConfigVersion < 1 || !IsName(definition.Id)
			|| !IsName(definition.Entry) || !definition.Entry.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
			throw new FormatException("ui.json requires version 1, a positive configVersion, an id and a flat HTML entry.");
		CheckSchema(definition.ConfigSchema, 0);
		if (definition.ConfigSchema.GetProperty("type").GetString() != "object")
			throw new FormatException("The root config schema must be an object.");
		return definition;
	}

	private static void CheckSchema(JsonElement schema, int depth)
	{
		if (depth > 8 || schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type)
			|| type.GetString() is not ("object" or "array" or "integer" or "number" or "string" or "boolean"))
			throw new FormatException("Unsupported config schema type or nesting depth.");
		foreach (var property in schema.EnumerateObject())
		{
			if (property.Name is not ("type" or "properties" or "required" or "additionalProperties" or "items" or "maxItems" or "minItems" or "minimum" or "maximum" or "maxLength"))
				throw new FormatException($"Unsupported config schema keyword '{property.Name}'.");
			if (property.Name == "properties")
			{
				if (property.Value.ValueKind != JsonValueKind.Object) throw new FormatException("properties must be an object.");
				foreach (var child in property.Value.EnumerateObject()) CheckSchema(child.Value, depth + 1);
			}
			if (property.Name == "items") CheckSchema(property.Value, depth + 1);
			if (property.Name == "required" && (property.Value.ValueKind != JsonValueKind.Array
				|| property.Value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)))
				throw new FormatException("required must be an array of property names.");
			if (property.Name == "additionalProperties" && property.Value.ValueKind != JsonValueKind.False)
				throw new FormatException("Only additionalProperties: false is supported.");
			if (property.Name is "maxItems" or "minItems" or "maxLength" && (!property.Value.TryGetInt32(out var bound) || bound < 0))
				throw new FormatException("Length bounds must be non-negative integers.");
			if (property.Name is "minimum" or "maximum" && !property.Value.TryGetDecimal(out _))
				throw new FormatException("Numeric bounds must be numbers.");
		}
		if (type.GetString() == "array" && !schema.TryGetProperty("items", out _))
			throw new FormatException("Array schemas require items.");
	}

	public string? ValidateConfig(int version, JsonElement config)
	{
		if (version != ConfigVersion) return $"Config version {version} does not match definition version {ConfigVersion}.";
		if (System.Text.Encoding.UTF8.GetByteCount(config.GetRawText()) > MaxConfigBytes) return "Config exceeds 32 KiB.";
		return Validate(ConfigSchema, config, "config");
	}

	private static string? Validate(JsonElement schema, JsonElement value, string path)
	{
		var type = schema.GetProperty("type").GetString();
		var correct = type switch
		{
			"object" => value.ValueKind == JsonValueKind.Object,
			"array" => value.ValueKind == JsonValueKind.Array,
			"integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
			"number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
			"string" => value.ValueKind == JsonValueKind.String,
			"boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
			_ => false
		};
		if (!correct) return $"{path} must be {type}.";
		if (type == "object")
		{
			if (schema.TryGetProperty("required", out var required))
				foreach (var field in required.EnumerateArray())
					if (!value.TryGetProperty(field.GetString()!, out _)) return $"{path}.{field.GetString()} is required.";
			schema.TryGetProperty("properties", out var properties);
			foreach (var field in value.EnumerateObject())
			{
				if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(field.Name, out var child))
				{
					if (Validate(child, field.Value, path + "." + field.Name) is { } error) return error;
				}
				else return $"Unknown config property {path}.{field.Name}.";
			}
		}
		if (type == "array")
		{
			var length = value.GetArrayLength();
			if (schema.TryGetProperty("maxItems", out var max) && length > max.GetInt32()) return $"{path} has too many items.";
			if (schema.TryGetProperty("minItems", out var min) && length < min.GetInt32()) return $"{path} has too few items.";
			var i = 0;
			foreach (var item in value.EnumerateArray())
				if (Validate(schema.GetProperty("items"), item, $"{path}[{i++}]") is { } error) return error;
		}
		if (type is "number" or "integer")
		{
			if (!value.TryGetDecimal(out var number)) return $"{path} is out of range.";
			if (schema.TryGetProperty("minimum", out var min) && number < min.GetDecimal()) return $"{path} is below minimum.";
			if (schema.TryGetProperty("maximum", out var max) && number > max.GetDecimal()) return $"{path} is above maximum.";
		}
		if (type == "string" && schema.TryGetProperty("maxLength", out var maxLength) && value.GetString()!.Length > maxLength.GetInt32())
			return $"{path} is too long.";
		return null;
	}
}
