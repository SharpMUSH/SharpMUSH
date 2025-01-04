using Json.More;
using Json.Patch;
using Json.Path;
using Json.Pointer;
using MoreLinq;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly Dictionary<string, Func<ImmutableSortedDictionary<string, CallState>, ValueTask<CallState>>> JsonFunctions = new()
	{
		{"null", JsonHelpers.NullJSON},
		{"boolean", JsonHelpers.BooleanJSON},
		{"string", JsonHelpers.StringJSON },
		{"markupstring", JsonHelpers.StringJSON }, // TODO: In PennMUSH, this uses their internal markup instead. This currently has no meaning for us yet.
		{"number", JsonHelpers.NumberJSON },
		{"array", JsonHelpers.ArrayJSON },
		{"object", JsonHelpers.ObjectJSON }
	};

	[SharpFunction(Name = "json", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> JSON(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> JsonFunctions.TryGetValue(MModule.plainText(parser.CurrentState.Arguments["0"].Message!).ToLower(), out var jsonFunction)
			? await jsonFunction(parser.CurrentState.ArgumentsOrdered)
			: new CallState(MModule.single("#-1 Invalid Type"));


	[SharpFunction(Name = "isjson", MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> IsJSON(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		try
		{
			using var jsonDoc = JsonDocument.Parse(parser.CurrentState.Arguments["0"].Message!.ToString());
			return ValueTask.FromResult(new CallState("1"));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState("0"));
		}
	}

	[SharpFunction(Name = "json_map", MinArgs = 2, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> json_map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "json_mod", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> json_mod(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var json = args["0"].Message!.ToString();
		var action = args["1"].Message!.ToPlainText().ToLower();
		var path = args["2"].Message!.ToPlainText();
		var json2 = args.Count > 3 ? args["3"].Message?.ToString() : null;

		try
		{
			var jsonDoc = JsonNode.Parse(json);
			var jsonPath = JsonPath.Parse(path);
			var jsonPointer = JsonPointer.Parse(jsonPath.AsJsonPointer());
			var jsonDoc2 = json2 is null ? null : JsonNode.Parse(json2);

			if ((action is "insert" or "replace" or "set" or "patch") && string.IsNullOrWhiteSpace(json2))
			{
				return new CallState("#-1 MISSING JSON2");
			}

			OneOf<JsonPatch,Error<string>> operation = action switch
			{
				"insert" => new JsonPatch(PatchOperation.Add(jsonPointer, jsonDoc2)),
				"replace" => new JsonPatch(PatchOperation.Replace(jsonPointer, jsonDoc2!)),
				"set" => new JsonPatch(PatchOperation.Replace(jsonPointer, jsonDoc2!)),
				// "patch" => JsonSerializer.Deserialize<JsonPatch>(jsonDoc2) ||  new JsonPatch(PatchOperation.(jsonPointer, jsonDoc2!)),
				"remove" => new JsonPatch(PatchOperation.Remove(jsonPointer)),
				"sort" => new JsonPatch(PatchOperation.Replace(jsonPointer, jsonDoc /* A sorted version! */)),
				_ => new Error<string>("Invalid Operation"),
			};

			if(operation.IsT1)
			{
				return new CallState("#-1 INVALID OPERATION");
			}

			var patched = operation.AsT0.Apply(jsonDoc);
			
			return patched.IsSuccess 
				? new CallState(patched.Result!.ToString()) 
				: new CallState($"#-1 PATCH FAILED: {patched.Error}");
		}
		catch (JsonException)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json_mod"));
		}
	}

	// TODO: Use JSON PATH PROPERLY
	[SharpFunction(Name = "json_query", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> json_query(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		if (args.IsEmpty)
		{
			return new CallState(string.Format(Errors.ErrorWrongArgumentsRange, "json_query", 1, int.MaxValue, args.Count));
		}

		var json = args["0"].Message!.ToPlainText();
		var action = args.Count > 1 ? args["1"].Message!.ToPlainText().ToLower() : "type";

		try
		{
			using var jsonDoc = JsonDocument.Parse(json);
			var rootElement = jsonDoc.RootElement;

			return action switch
			{
				"type" => new CallState(JsonHelpers.GetJsonType(rootElement)),
				"size" => new CallState(JsonHelpers.GetJsonSize(rootElement)),
				"exists" => new CallState(JsonHelpers.JsonExists(rootElement, args.Skip(2).Select(a => a.Value.Message!.ToPlainText()).ToArray()) ? "1" : "0"),
				"get" => new CallState(JsonHelpers.JsonGet(rootElement, args.Skip(2).Select(a => a.Value.Message!.ToString()).ToArray())),
				"extract" => new CallState(JsonHelpers.JsonExtract(rootElement, args["2"].Message!.ToString())),
				"unescape" => rootElement.ValueKind == JsonValueKind.String
						? new CallState(rootElement.GetString() ?? string.Empty)
						: new CallState("#-1 INVALID TYPE"),
				_ => new CallState("#-1 INVALID ACTION")
			};
		}
		catch (JsonException)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json_query"));
		}
	}

	[SharpFunction(Name = "OOB", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> oob(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}