using Json.Patch;
using Json.Path;
using Json.Pointer;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using static SharpMUSH.Library.Services.Interfaces.LocateFlags;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly Dictionary<string, Func<ImmutableSortedDictionary<string, CallState>, ValueTask<CallState>>> JsonFunctions = new()
	{
		{"null", JsonHelpers.NullJSON},
		{"boolean", JsonHelpers.BooleanJSON},
		{"string", JsonHelpers.StringJSON },
		{"markupstring", JsonHelpers.StringJSON },
		{"number", JsonHelpers.NumberJSON },
		{"array", JsonHelpers.ArrayJSON },
		{"object", JsonHelpers.ObjectJSON }
	};

	[SharpFunction(Name = "json", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["expression..."])]
	public static async ValueTask<CallState> JSON(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> JsonFunctions.TryGetValue(MModule.plainText(parser.CurrentState.Arguments["0"].Message!).ToLower(), out var jsonFunction)
			? await jsonFunction(parser.CurrentState.ArgumentsOrdered)
			: new CallState(MModule.single("#-1 Invalid Type"));


	[SharpFunction(Name = "isjson", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
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

	[SharpFunction(Name = "json_map", MinArgs = 2, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["attribute", "json", "path"])]
	public static async ValueTask<CallState> json_map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = MModule.plainText(rawAttrArg)!;

		var jsonStr = parser.CurrentState.Arguments["1"].Message!.ToString();
		var osep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));

		var userArgs = new Dictionary<string, CallState>();
		for (int i = 3; i < parser.CurrentState.Arguments.Count; i++)
		{
			userArgs[i.ToString()] = parser.CurrentState.Arguments[i.ToString()];
		}

		// Resolved attribute text for the standard (non-lambda) path; only set in the if-block below.
		MString attrValue = MModule.empty();

		// Helper to evaluate a function call (attribute or lambda) with a given args dict.
		// For #lambda / #apply, EvaluateAttributeFunctionAsync handles the special prefix.
		// For regular attribute references, we use the pre-resolved attrValue via FunctionParse.
		async ValueTask<MString> EvalWithArgs(Dictionary<string, CallState> callArgs)
		{
			if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
			{
				return await AttributeService!.EvaluateAttributeFunctionAsync(parser, executor, rawAttrArg, callArgs);
			}

			var callParser = parser.Push(parser.CurrentState with
			{
				Arguments = callArgs,
				EnvironmentRegisters = callArgs
			});
			return (await callParser.FunctionParse(attrValue))!.Message!;
		}

		if (!HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known;
			var objAttr = HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
			if (objAttr is { IsT1: true, AsT1: false })
			{
				return new CallState(Errors.ErrorObjectAttributeString);
			}

			var (dbref, attrName) = objAttr.AsT0;
			dbref ??= executor.Object().DBRef.ToString();

			var locate = await LocateService!.LocateAndNotifyIfInvalid(parser, enactor, executor, dbref, LocateFlags.All);
			if (!locate.IsValid())
			{
				return CallState.Empty;
			}

			var located = locate.WithoutError().WithoutNone();
			var maybeAttr = await AttributeService!.GetAttributeAsync(executor, located, attrName,
				mode: IAttributeService.AttributeMode.Execute, parent: true);

			if (maybeAttr.IsNone)
			{
				return new CallState(Errors.ErrorNoSuchAttribute);
			}

			if (maybeAttr.IsError)
			{
				return new CallState(maybeAttr.AsError.Value);
			}

			attrValue = maybeAttr.AsAttribute.Last().Value;
		}

		try
		{
			using var jsonDoc = JsonDocument.Parse(jsonStr);
			var rootElement = jsonDoc.RootElement;

			var result = new List<MString>();

			switch (rootElement.ValueKind)
			{
				case JsonValueKind.Null:
				case JsonValueKind.True:
				case JsonValueKind.False:
				case JsonValueKind.String:
				case JsonValueKind.Number:
					var args = new Dictionary<string, CallState>
					{
						{ "0", new CallState(JsonHelpers.GetJsonType(rootElement)) },
						{ "1", new CallState(rootElement.GetRawText()) }
					};
					foreach (var ua in userArgs) args[ua.Key] = ua.Value;
					result.Add(await EvalWithArgs(args));
					break;

				case JsonValueKind.Array:
					var arrayIndex = 0;
					foreach (var element in rootElement.EnumerateArray())
					{
						var arrayArgs = new Dictionary<string, CallState>
						{
							{ "0", new CallState(JsonHelpers.GetJsonType(element)) },
							{ "1", new CallState(element.GetRawText()) },
							{ "2", new CallState(arrayIndex.ToString()) }
						};
						foreach (var ua in userArgs) arrayArgs[ua.Key] = ua.Value;
						result.Add(await EvalWithArgs(arrayArgs));
						arrayIndex++;
					}
					break;

				case JsonValueKind.Object:
					foreach (var property in rootElement.EnumerateObject())
					{
						var objArgs = new Dictionary<string, CallState>
						{
							{ "0", new CallState(JsonHelpers.GetJsonType(property.Value)) },
							{ "1", new CallState(property.Value.GetRawText()) },
							{ "2", new CallState(property.Name) }
						};
						foreach (var ua in userArgs) objArgs[ua.Key] = ua.Value;
						result.Add(await EvalWithArgs(objArgs));
					}
					break;
				case JsonValueKind.Undefined:
				default:
					throw new JsonException();
			}

			return new CallState(MModule.multipleWithDelimiter(osep, result));
		}
		catch (JsonException ex)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json_map") + $": {ex.Message}");
		}
	}

	[SharpFunction(Name = "json_mod", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["json", "path", "value"])]
	public static async ValueTask<CallState> json_mod(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var json = args["0"].Message!.ToString();
		var action = args["1"].Message!.ToPlainText().ToLower();
		var arg2 = args["2"].Message!.ToPlainText();
		var json2 = args.Count > 3 ? args["3"].Message?.ToString() : null;

		try
		{
			var jsonDoc = JsonNode.Parse(json);

			// patch: arg2 is the merge-patch document applied to the root (no JSONPath needed)
			if (action == "patch")
			{
				if (string.IsNullOrWhiteSpace(arg2))
				{
					return new CallState("#-1 MISSING JSON2");
				}
				var patchDoc = JsonNode.Parse(arg2);
				var merged = ApplyMergePatch(jsonDoc, patchDoc!);
				return new CallState(merged?.ToJsonString() ?? "null");
			}

			// sort: arg2 is a JSONPath selector applied to each array element to get the sort key
			if (action == "sort")
			{
				if (jsonDoc is not JsonArray arr)
				{
					return new CallState("#-1 NOT AN ARRAY");
				}

				var selectorPath = JsonPath.Parse(arg2);
				var sorted = arr
					.Select(item => item?.DeepClone())
					.OrderBy(item =>
					{
						var evalResult = selectorPath.Evaluate(item);
						if (evalResult.Matches == null || evalResult.Matches.Count == 0)
							return (string?)null;
						var val = evalResult.Matches[0].Value;
						return val?.ToString();
					}, StringComparer.Ordinal)
					.ToList();

				var sortedArray = new JsonArray(sorted.ToArray());
				return new CallState(sortedArray.ToJsonString());
			}

			var jsonPath = JsonPath.Parse(arg2);

			// Evaluate path to see if it exists
			var pathResult = jsonPath.Evaluate(jsonDoc);
			var pathExists = pathResult.Matches != null && pathResult.Matches.Count > 0;

			// For modification operations with found matches, we need exactly one
			if (pathExists && pathResult.Matches!.Count > 1)
			{
				return new CallState("#-1 PATH MUST BE SINGULAR");
			}

			var jsonDoc2 = json2 is null ? null : JsonNode.Parse(json2);

			if (action is "insert" or "replace" or "set" && string.IsNullOrWhiteSpace(json2))
			{
				return new CallState("#-1 MISSING JSON2");
			}

			if (action == "insert")
			{
				// insert: only add if path does NOT exist
				if (pathExists)
				{
					// Key already exists — return unchanged
					return new CallState(jsonDoc?.ToJsonString() ?? "null");
				}
				// Path doesn't exist yet — use JSON Pointer derived from JSONPath to add
				var insertPointer = JsonPathToPointer(arg2);
				if (insertPointer is null)
					return new CallState("#-1 PATH NOT FOUND");
				var insertPatch = new JsonPatch(PatchOperation.Add(insertPointer.Value, jsonDoc2));
				var insertResult = insertPatch.Apply(jsonDoc);
				return insertResult.IsSuccess
					? new CallState(insertResult.Result!.ToJsonString())
					: new CallState(jsonDoc?.ToJsonString() ?? "null");
			}

			if (action == "set")
			{
				// set: add or replace — use JSON Pointer from path (even if new)
				// If path exists, replace it; if not, add it
				JsonPointer setPointer;
				if (pathExists)
				{
					setPointer = JsonPointer.Parse(pathResult.Matches![0].Location!.AsJsonPointer());
				}
				else
				{
				var derived = JsonPathToPointer(arg2);
				if (derived is null)
					return new CallState("#-1 PATH NOT FOUND");
				setPointer = derived.Value;
				}
				var setPatch = new JsonPatch(PatchOperation.Add(setPointer, jsonDoc2));
				var setResult = setPatch.Apply(jsonDoc);
				return setResult.IsSuccess
					? new CallState(setResult.Result!.ToJsonString())
					: new CallState($"#-1 SET FAILED: {setResult.Error}");
			}

			if (action == "replace")
			{
				if (!pathExists)
				{
					// replace of nonexistent key → no-op, return original
					return new CallState(jsonDoc?.ToJsonString() ?? "null");
				}
				var replacePointer = JsonPointer.Parse(pathResult.Matches![0].Location!.AsJsonPointer());
				var replacePatch = new JsonPatch(PatchOperation.Replace(replacePointer, jsonDoc2!));
				var replaceResult = replacePatch.Apply(jsonDoc);
				return replaceResult.IsSuccess
					? new CallState(replaceResult.Result!.ToJsonString())
					: new CallState($"#-1 REPLACE FAILED: {replaceResult.Error}");
			}

			if (action == "remove")
			{
				if (!pathExists)
				{
					// remove of nonexistent key → no-op, return original
					return new CallState(jsonDoc?.ToJsonString() ?? "null");
				}
				var removePointer = JsonPointer.Parse(pathResult.Matches![0].Location!.AsJsonPointer());
				var removePatch = new JsonPatch(PatchOperation.Remove(removePointer));
				var removeResult = removePatch.Apply(jsonDoc);
				return removeResult.IsSuccess
					? new CallState(removeResult.Result!.ToJsonString())
					: new CallState($"#-1 REMOVE FAILED: {removeResult.Error}");
			}

			return new CallState("#-1 INVALID OPERATION");
		}
		catch (JsonException)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json_mod"));
		}
	}

	private static JsonNode? ApplyMergePatch(JsonNode? target, JsonNode patch)
	{
		if (patch is not JsonObject patchObj)
		{
			return patch.DeepClone();
		}

		if (target is not JsonObject targetObj)
		{
			targetObj = new JsonObject();
		}
		else
		{
			targetObj = targetObj.DeepClone() as JsonObject ?? new JsonObject();
		}

		foreach (var kvp in patchObj)
		{
			if (kvp.Value is null)
			{
				targetObj.Remove(kvp.Key);
			}
			else
			{
				if (targetObj.TryGetPropertyValue(kvp.Key, out var existingValue))
				{
					targetObj[kvp.Key] = ApplyMergePatch(existingValue, kvp.Value);
				}
				else
				{
					targetObj[kvp.Key] = kvp.Value.DeepClone();
				}
			}
		}

		return targetObj;
	}

	/// <summary>
	/// Converts a simple JSONPath like $.key, $.key.subkey, $.key[0] to a JSON Pointer like /key, /key/subkey, /key/0.
	/// Returns null if the path is too complex to convert directly.
	/// </summary>
	private static JsonPointer? JsonPathToPointer(string jsonPath)
	{
		if (string.IsNullOrEmpty(jsonPath) || jsonPath == "$")
			return JsonPointer.Parse("/");

		// Must start with "$"
		if (!jsonPath.StartsWith("$"))
			return null;

		// Remove leading "$"
		var rest = jsonPath[1..];
		var segments = new List<string>();

		var i = 0;
		while (i < rest.Length)
		{
			if (rest[i] == '.')
			{
				i++;
				// Read property name until next '.' or '['
				var end = rest.IndexOfAny(['.', '['], i);
				var name = end < 0 ? rest[i..] : rest[i..end];
				if (string.IsNullOrEmpty(name)) return null;
				// Escape JSON Pointer special chars
				segments.Add(name.Replace("~", "~0").Replace("/", "~1"));
				i = end < 0 ? rest.Length : end;
			}
			else if (rest[i] == '[')
			{
				i++;
				var close = rest.IndexOf(']', i);
				if (close < 0) return null;
				var index = rest[i..close];
				segments.Add(index);
				i = close + 1;
			}
			else
			{
				return null;
			}
		}

		return segments.Count == 0
			? JsonPointer.Parse("/")
			: JsonPointer.Parse("/" + string.Join("/", segments));
	}

	[SharpFunction(Name = "json_query", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["json", "path"])]
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

			var errorBadArg = string.Format(Errors.ErrorBadArgumentFormat, "json_query");

			if (action == "type")
				return new CallState(JsonHelpers.GetJsonType(rootElement));

			if (action == "size")
				return new CallState(JsonHelpers.GetJsonSize(rootElement));

			if (action == "exists")
			{
				var existsPath = args.Skip(2).Select(a => a.Value.Message!.ToPlainText()).ToArray();
				var existsResult = JsonHelpers.JsonExists(rootElement, existsPath);
				return existsResult switch
				{
					null => new CallState(errorBadArg),
					true => new CallState("1"),
					false => new CallState("0"),
				};
			}

			if (action == "get")
			{
				var getPath = args.Skip(2).Select(a => a.Value.Message!.ToPlainText()).ToArray();
				var getResult = JsonHelpers.JsonGet(rootElement, getPath);
				return getResult is null
					? new CallState(errorBadArg)
					: new CallState(getResult);
			}

			if (action == "extract")
			{
				var extractPath = args.Count > 2 ? args["2"].Message!.ToPlainText() : "$";
				var extractResult = JsonHelpers.JsonExtract(rootElement, extractPath);
				return extractResult is null
					? new CallState(errorBadArg)
					: new CallState(extractResult);
			}

			if (action == "unescape")
				return rootElement.ValueKind == JsonValueKind.String
					? new CallState(rootElement.GetString() ?? string.Empty)
					: new CallState("#-1 INVALID TYPE");

			return new CallState("#-1 INVALID ACTION");
		}
		catch (JsonException)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json_query"));
		}
	}

	[SharpFunction(Name = "oob", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["command", "arguments..."])]
	public static async ValueTask<CallState> oob(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known;

		var playersArg = MModule.plainText(parser.CurrentState.Arguments["0"].Message!);
		var package = MModule.plainText(parser.CurrentState.Arguments["1"].Message!);
		var message = parser.CurrentState.Arguments.TryGetValue("2", out var msgState)
			? msgState.Message?.ToString() ?? ""
			: "";

		var players = ArgHelpers.NameListString(playersArg);

		if (!string.IsNullOrWhiteSpace(message))
		{
			try
			{
				using var _ = JsonDocument.Parse(message);
			}
			catch (JsonException)
			{
				return new CallState("#-1 INVALID JSON MESSAGE");
			}
		}

		var isWizard = await executor.IsWizard();
		var hasSendOOBPower = await ArgHelpers.HasObjectPowers(executor.Object(), "Send_OOB");

		int sentCount = 0;

		foreach (var playerStr in players)
		{
			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				executor,
				playerStr,
				LocateFlags.All);

			if (!locate.IsValid())
			{
				continue;
			}

			var located = locate.WithoutError().WithoutNone();

			if (!located.IsPlayer)
			{
				continue;
			}

			var isSelf = executor.Object().DBRef == located.Object().DBRef;

			if (!isWizard && !isSelf && !hasSendOOBPower)
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			await foreach (var connection in ConnectionService!.Get(located.Object().DBRef))
			{
				if (connection.Metadata.GetValueOrDefault("GMCP", "0") != "1")
				{
					continue;
				}

				await MessageBus!.Publish(new GMCPOutputMessage(
					connection.Handle,
					package,
					message));

				sentCount++;
			}
		}

		return new CallState(sentCount.ToString());
	}

	[SharpFunction(Name = "WEBSOCKET_JSON", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["json", "player"])]
	public static async ValueTask<CallState> WebSocketJSON(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Send JSON data via websocket - similar to wsjson()
		var jsonContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		AnySharpObject target;
		if (parser.CurrentState.Arguments.TryGetValue("1", out var targetArg))
		{
			var targetRef = targetArg.Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				targetRef,
				PlayersPreference | AbsoluteMatch);

			if (locateResult.IsError)
			{
				return new CallState(locateResult.AsError);
			}

			target = locateResult.AsAnyObject;
		}
		else
		{
			target = executor;
		}

		// TODO: Actual websocket/out-of-band JSON communication is planned for future release.
		// For now, this returns an error as the feature requires websocket support.
		//
		// Full implementation requirements:
		// 1. Add websocket support to ConnectionService
		// 2. Implement GMCP (Generic MUD Communication Protocol) for JSON
		// 3. Support standard GMCP packages and custom packages
		// 4. Add JSON schema validation for safety
		// 5. Implement bidirectional JSON communication
		//
		// When implemented, this will send JSON through GMCP/websocket channel
		// Placeholder - returns empty string as OOB data doesn't display in-band
		return CallState.Empty;
	}
}