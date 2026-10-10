using Json.Path;
using MoreLinq;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Functions;

public static class JsonHelpers
{
	internal static readonly JsonSerializerOptions RelaxedJsonOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static ValueTask<CallState> NullJSON(OrderedArguments args)
	{
		if (args.Count == 1)
		{
			return ValueTask.FromResult(new CallState("null"));
		}
		if (args.Count == 2 && args["1"].Message.ToPlainText().Equals("null", StringComparison.OrdinalIgnoreCase))
		{
			return ValueTask.FromResult(new CallState("null"));
		}
		return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArgument));
	}

	public static ValueTask<CallState> BooleanJSON(OrderedArguments args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.WrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = args["1"].Message.ToPlainText();

		return entry switch
		{
			not "1" and not "0" and not "false" and not "true" => ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidValue)),
			_ => ValueTask.FromResult(new CallState(entry is "1" or "true" ? "true" : "false"))
		};
	}

	public static ValueTask<CallState> StringJSON(OrderedArguments args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.WrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = args["1"].Message;

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(entry!.ToString(), RelaxedJsonOptions)));
	}

	/// <summary>
	/// <c>json(markupstring, &lt;text&gt;)</c>: the text with its colour and markup kept, as a JSON string holding
	/// the serialized MString a portal <c>mstring</c> field or timeline row draws. <c>json(string)</c> keeps only
	/// the plain text.
	/// </summary>
	public static ValueTask<CallState> MarkupStringJSON(OrderedArguments args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.WrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = args["1"].Message;

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(MarkupTextSerializer.Serialize(entry), RelaxedJsonOptions)));
	}

	public static ValueTask<CallState> NumberJSON(OrderedArguments args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.WrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = args["1"].Message.ToPlainText();
		if (!decimal.TryParse(entry, out var value))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Number));
		}

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(value)));
	}

	public static ValueTask<CallState> ArrayJSON(OrderedArguments args)
	{
		if (args.Count < 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.WrongArgumentsRange, "json", 2, int.MaxValue, args.Count)));
		}

		try
		{
			var elements = args
				.Skip(1)
				.Select(x => ParseElement(x.Value.Message.ToPlainText()));

			return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(elements)));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "json")));
		}
	}

	/// <summary>
	/// One JSON value, cloned out of its document so the document and its pooled buffer are returned
	/// as soon as it is read rather than when the finalizer gets to it.
	/// </summary>
	private static JsonElement ParseElement(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	public static ValueTask<CallState> ObjectJSON(OrderedArguments args)
	{
		if (args.Count < 3)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.WrongArgumentsRange, "json", 2, int.MaxValue, args.Count)));
		}

		if (args.Count % 2 == 0)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.GotEvenArgs, "json")));
		}

		var pairs = args.Values.Select(x => x.Message).Skip(1).Chunk(2).ToList();
		var duplicateKeys = pairs.Select(x => x[0].ToPlainText()).Duplicates().ToList();

		if (duplicateKeys.Count > 0)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.DuplicateKeysFormat, string.Join(", ", duplicateKeys))));
		}

		try
		{
			var dictionary = pairs.ToDictionary(x => x[0].ToPlainText(), x => ParseElement(x[1].ToString()));
			return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(dictionary)));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "json")));
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

	/// <summary>
	/// json_fill()'s work: <paramref name="json"/> with each JSON Pointer in <paramref name="fills"/>
	/// given its value, typed by the value already there (see <see cref="FillValue"/>), or the error for
	/// the first fill that cannot be made.
	/// </summary>
	public static Result<string> Fill(string json, IEnumerable<(string Pointer, string Value)> fills)
	{
		JsonNode? root;
		try
		{
			root = JsonNode.Parse(json);
		}
		catch (JsonException)
		{
			return new Error<string>(string.Format(ErrorMessages.Returns.BadArgumentFormat, "json_fill"));
		}

		foreach (var (pointer, value) in fills)
		{
			if (pointer.Length == 0)
			{
				// RFC 6901: the empty pointer names the whole document, so its fill replaces the root.
				switch (FillValue(root, pointer, value))
				{
					case JsonFill fill:
						root = fill.Node;
						continue;
					case Error<string> rootError:
						return rootError;
				}
			}
			if (FillOne(root, pointer, value) is Error<string> error) return error;
		}
		return root?.ToJsonString(RelaxedJsonOptions) ?? "null";
	}

	private static Result<Success> FillOne(JsonNode? root, string pointer, string value)
		=> FindSlot(root, pointer) switch
		{
			JsonSlot slot => FillValue(slot.Current, pointer, value) switch
			{
				JsonFill fill => slot.Set(fill.Node),
				Error<string> error => error
			},
			Error<string> error => error
		};

	/// <summary>
	/// The member or element an RFC 6901 JSON Pointer names. It must already exist: json_fill fills a
	/// template, so a pointer that names nothing is a mistake in the pointer, not a request to add.
	/// </summary>
	private static Result<JsonSlot> FindSlot(JsonNode? root, string pointer)
	{
		var notFound = new Error<string>(string.Format(ErrorMessages.Returns.JsonPathNotFoundFormat, pointer));
		if (!pointer.StartsWith('/')) return notFound;

		var segments = pointer[1..].Split('/').Select(segment => segment.Replace("~1", "/").Replace("~0", "~")).ToArray();
		var container = root;
		foreach (var segment in segments[..^1])
		{
			if (Child(container, segment) is not JsonFill { Node: { } child }) return notFound;
			container = child;
		}

		return container is not null && Child(container, segments[^1]) is JsonFill last
			? new JsonSlot(container, segments[^1], last.Node)
			: notFound;
	}

	/// <summary>The member or element <paramref name="segment"/> names, or null when there is none.</summary>
	private static JsonFill? Child(JsonNode? container, string segment) => container switch
	{
		JsonObject obj when obj.TryGetPropertyValue(segment, out var member) => new JsonFill(member),
		JsonArray array when ArrayIndex(array, segment) is { } index => new JsonFill(array[index]),
		_ => null
	};

	private static int? ArrayIndex(JsonArray array, string segment)
		=> int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count
			? index
			: null;

	/// <summary>
	/// <paramref name="value"/> as the JSON that replaces <paramref name="current"/>: a string where the
	/// template holds a string, a number where it holds a number, <c>true</c>/<c>false</c> (or 1/0) where it
	/// holds a boolean, and the value read as JSON where it holds an object, an array or null.
	/// </summary>
	private static Result<JsonFill> FillValue(JsonNode? current, string pointer, string value)
		=> current?.GetValueKind() switch
		{
			JsonValueKind.String => new JsonFill(JsonValue.Create(value)),
			JsonValueKind.Number => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
				? new JsonFill(JsonValue.Create(number))
				: new Error<string>(string.Format(ErrorMessages.Returns.JsonFillNumberFormat, pointer)),
			JsonValueKind.True or JsonValueKind.False => value switch
			{
				"1" or "true" => new JsonFill(JsonValue.Create(true)),
				"0" or "false" => new JsonFill(JsonValue.Create(false)),
				_ => new Error<string>(string.Format(ErrorMessages.Returns.JsonFillBooleanFormat, pointer))
			},
			_ => ParseFill(pointer, value)
		};

	private static Result<JsonFill> ParseFill(string pointer, string value)
	{
		try
		{
			return new JsonFill(JsonNode.Parse(value));
		}
		catch (JsonException)
		{
			return new Error<string>(string.Format(ErrorMessages.Returns.JsonFillJsonFormat, pointer));
		}
	}

	/// <summary>A JSON value to write, where null is JSON's <c>null</c>.</summary>
	private readonly record struct JsonFill(JsonNode? Node);

	/// <summary>Where json_fill writes one value: the object or array holding it, and its key or index.</summary>
	private readonly record struct JsonSlot(JsonNode Container, string Key, JsonNode? Current)
	{
		public Success Set(JsonNode? value)
		{
			if (Container is JsonArray array)
				array[int.Parse(Key, CultureInfo.InvariantCulture)] = value;
			else
				Container[Key] = value;
			return new Success();
		}
	}
}
