using Json.Path;
using MoreLinq;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SharpMUSH.Implementation.Functions;

public static class JsonHelpers
{
	private static readonly JsonSerializerOptions RelaxedJsonOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static ValueTask<CallState> NullJSON(ImmutableSortedDictionary<string, CallState> args)
	{
		if (args.Count == 1)
		{
			return ValueTask.FromResult(new CallState("null"));
		}
		if (args.Count == 2 && MModule.plainText(args["1"].Message).Equals("null", StringComparison.OrdinalIgnoreCase))
		{
			return ValueTask.FromResult(new CallState("null"));
		}
		return ValueTask.FromResult(new CallState("#-1"));
	}

	public static ValueTask<CallState> BooleanJSON(ImmutableSortedDictionary<string, CallState> args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = MModule.plainText(args["1"].Message);

		return entry switch
		{
			not "1" and not "0" and not "false" and not "true" => ValueTask.FromResult(new CallState("#-1 INVALID VALUE")),
			_ => ValueTask.FromResult(new CallState(entry is "1" or "true" ? "true" : "false"))
		};
	}

	public static ValueTask<CallState> StringJSON(ImmutableSortedDictionary<string, CallState> args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = args["1"].Message;

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(entry!.ToString(), RelaxedJsonOptions)));
	}

	public static ValueTask<CallState> NumberJSON(ImmutableSortedDictionary<string, CallState> args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = MModule.plainText(args["1"].Message);
		if (!decimal.TryParse(entry, out var value))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumber));
		}

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(value)));
	}

	public static ValueTask<CallState> ArrayJSON(ImmutableSortedDictionary<string, CallState> args)
	{
		if (args.Count < 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, int.MaxValue, args.Count)));
		}

		try
		{
			var sortedArgs = args
				.Skip(1)
				.Select(x => JsonDocument.Parse(x.Value.Message!.ToString()).RootElement);

			return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(sortedArgs)));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json")));
		}
	}

	public static ValueTask<CallState> ObjectJSON(ImmutableSortedDictionary<string, CallState> args)
	{
		if (args.Count < 3)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, int.MaxValue, args.Count)));
		}

		if (args.Count % 2 == 0)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorGotEvenArgs, "json")));
		}

		var sortedArgs = args.Select(x => x.Value.Message!).Skip(1);
		var chunkedArgs = sortedArgs.Chunk(2).ToList();
		var duplicateKeys = chunkedArgs.Select(x => x[0].ToPlainText()).Duplicates().ToList();

		if (duplicateKeys.Any())
		{
			return ValueTask.FromResult(new CallState($"#-1 DUPLICATE KEYS: {string.Join(", ", duplicateKeys)}"));
		}

		try
		{
			var dictionary = chunkedArgs.ToDictionary(x => x[0].ToPlainText(), x => JsonDocument.Parse(x[1].ToString()).RootElement);
			return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(dictionary)));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json")));
		}
	}

	public static string GetJsonType(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Object => "object",
		JsonValueKind.Array => "array",
		JsonValueKind.String => "string",
		JsonValueKind.Number => "number",
		JsonValueKind.True => "boolean",
		JsonValueKind.False => "boolean",
		JsonValueKind.Null => "null",
		_ => "unknown"
	};

	public static int GetJsonSize(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Object => element.GetPropertyCount(),
		JsonValueKind.Array => element.GetArrayLength(),
		JsonValueKind.String => 1,
		JsonValueKind.Number => 1,
		JsonValueKind.True => 1,
		JsonValueKind.False => 1,
		JsonValueKind.Null => 0,
		_ => 0
	};

	/// <summary>
	/// Returns null if the root element is a scalar (not object/array) and a non-empty path is given.
	/// Returns true/false for path traversal success.
	/// </summary>
	public static bool? JsonExists(JsonElement element, string[] path)
	{
		// Scalars with a path → error
		if (path.Length > 0 && element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array)
			return null;

		foreach (var segment in path)
		{
			if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var property))
			{
				element = property;
			}
			else if (element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index) && index >= 0 && index < element.GetArrayLength())
			{
				element = element[index];
			}
			else
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// Returns null to signal #-1 (scalar root with path, or invalid JSON),
	/// empty string for missing key/index, or raw JSON text of found element.
	/// </summary>
	public static string? JsonGet(JsonElement element, string[] path)
	{
		// Scalars with a path → error
		if (path.Length > 0 && element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array)
			return null;

		foreach (var segment in path)
		{
			if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var property))
			{
				element = property;
			}
			else if (element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index) && index >= 0 && index < element.GetArrayLength())
			{
				element = element[index];
			}
			else
			{
				// Missing key or out-of-bounds index → empty string (not error)
				return string.Empty;
			}
		}
		return element.GetRawText();
	}

	/// <summary>
	/// Evaluates a JSONPath expression (e.g. $.a, $.c[1]) against a JSON element.
	/// Returns the raw text of the result, empty string if path not found, or null on error.
	/// Special case: path "$" returns the entire element (as unquoted string for string values).
	/// </summary>
	public static string? JsonExtract(JsonElement element, string path)
	{
		// "$" alone means the root value itself; for strings, return unquoted value
		if (path == "$")
		{
			return element.ValueKind == JsonValueKind.String
				? element.GetString() ?? string.Empty
				: element.GetRawText();
		}

		// Use Json.Path for proper JSONPath evaluation
		try
		{
			var jsonPath = Json.Path.JsonPath.Parse(path);
			// Need to parse as JsonNode for Json.Path
			var jsonText = element.GetRawText();
			var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(jsonText);
			var result = jsonPath.Evaluate(jsonNode);
			if (result.Matches == null || result.Matches.Count == 0)
				return string.Empty;
			var val = result.Matches[0].Value;
			if (val is null)
				return string.Empty;
			// For strings: return unquoted value; for others: return raw JSON
			if (val is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var strVal))
				return strVal;
			return val.ToJsonString();
		}
		catch
		{
			return null;
		}
	}
}