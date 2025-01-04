using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Path;
using MoreLinq;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public static class JsonHelpers
{
	public static ValueTask<CallState> NullJSON(ImmutableSortedDictionary<string, CallState> args)
		=> args.Count > 2
			? ValueTask.FromResult(new CallState(string.Format(Errors.ErrorTooManyArguments, "json", 2, args.Count)))
			: ValueTask.FromResult(new CallState("null"));

	public static ValueTask<CallState> BooleanJSON(ImmutableSortedDictionary<string, CallState> args)
	{
		if (args.Count != 2)
		{
			return ValueTask.FromResult(new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json", 2, 2, args.Count)));
		}

		var entry = MModule.plainText(args["1"].Message);

		return entry switch
		{
			not "1" and not "0" and not "false" and not "true" => ValueTask.FromResult(new CallState("#-1 INVALD VALUE")),
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

		return ValueTask.FromResult(new CallState(JsonSerializer.Serialize(entry!.ToString())));
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

	public static bool JsonExists(JsonElement element, string[] path)
	{
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

	public static string JsonGet(JsonElement element, string[] path)
	{
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
				return "#-1";
			}
		}
		return element.GetRawText();
	}

	public static string JsonExtract(JsonElement element, string path)
	{
		var currentElement = element;
		var segments = path.Split('.');
		foreach (var segment in segments)
		{
			if (currentElement.ValueKind == JsonValueKind.Object && currentElement.TryGetProperty(segment, out var property))
			{
				currentElement = property;
			}
			else if (currentElement.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index) && index >= 0 && index < currentElement.GetArrayLength())
			{
				currentElement = currentElement[index];
			}
			else
			{
				return "0";
			}
		}

		return currentElement.ValueKind switch
		{
			JsonValueKind.String => currentElement.GetString() ?? string.Empty,
			JsonValueKind.True => "1",
			JsonValueKind.False => "0",
			_ => currentElement.GetRawText()
		};
	}
}