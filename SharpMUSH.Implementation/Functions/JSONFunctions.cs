using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Patch;
using Json.Path;
using Json.Pointer;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
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
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known;
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.Object().DBRef.ToString();

		var locate = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			enactor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService!.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(Errors.ErrorNoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;
		
		var jsonStr = parser.CurrentState.Arguments["1"].Message!.ToString();
		var osep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));

		var userArgs = new Dictionary<string, CallState>();
		for (int i = 3; i < parser.CurrentState.Arguments.Count; i++)
		{
			userArgs[i.ToString()] = parser.CurrentState.Arguments[i.ToString()];
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
					foreach (var ua in userArgs)
					{
						args[ua.Key] = ua.Value;
					}
					var newParser = parser.Push(parser.CurrentState with
					{
						Arguments = args,
						EnvironmentRegisters = args
					});
					result.Add((await newParser.FunctionParse(attrValue))!.Message!);
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
						foreach (var ua in userArgs)
						{
							arrayArgs[ua.Key] = ua.Value;
						}
						var arrayParser = parser.Push(parser.CurrentState with
						{
							Arguments = arrayArgs,
							EnvironmentRegisters = arrayArgs
						});
						result.Add((await arrayParser.FunctionParse(attrValue))!.Message!);
						arrayIndex++;
					}
					break;

				case JsonValueKind.Object:
					foreach (var objArgs in rootElement
						         .EnumerateObject()
						         .Select(property => new Dictionary<string, CallState>
					         {
						         { "0", new CallState(JsonHelpers.GetJsonType(property.Value)) },
						         { "1", new CallState(property.Value.GetRawText()) },
						         { "2", new CallState(property.Name) }
					         }))
					{
						foreach (var ua in userArgs)
						{
							objArgs[ua.Key] = ua.Value;
						}
						var objParser = parser.Push(parser.CurrentState with
						{
							Arguments = objArgs,
							EnvironmentRegisters = objArgs
						});
						result.Add((await objParser.FunctionParse(attrValue))!.Message!);
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
		var path = args["2"].Message!.ToPlainText();
		var json2 = args.Count > 3 ? args["3"].Message?.ToString() : null;

		try
		{
			var jsonDoc = JsonNode.Parse(json);
			var jsonPath = JsonPath.Parse(path);
			var jsonPointer = JsonPointer.Parse(jsonPath.AsJsonPointer());
			var jsonDoc2 = json2 is null ? null : JsonNode.Parse(json2);

			if (action is "insert" or "replace" or "set" or "patch" && string.IsNullOrWhiteSpace(json2))
			{
				return new CallState("#-1 MISSING JSON2");
			}

			if (action == "patch")
			{
				var target = jsonPointer.TryEvaluate(jsonDoc, out var targetNode) ? targetNode : null;
				if (target == null)
				{
					return new CallState("#-1 PATH NOT FOUND");
				}

				var mergedNode = ApplyMergePatch(target, jsonDoc2!);
				var patchOp = new JsonPatch(PatchOperation.Replace(jsonPointer, mergedNode));
				var patched = patchOp.Apply(jsonDoc);
				
				return patched.IsSuccess 
					? new CallState(patched.Result!.ToString()) 
					: new CallState($"#-1 PATCH FAILED: {patched.Error}");
			}

			OneOf<JsonPatch,Error<string>> operation = action switch
			{
				"insert" => new JsonPatch(PatchOperation.Add(jsonPointer, jsonDoc2)),
				"replace" => new JsonPatch(PatchOperation.Replace(jsonPointer, jsonDoc2!)),
				"set" => new JsonPatch(PatchOperation.Replace(jsonPointer, jsonDoc2!)),
				"remove" => new JsonPatch(PatchOperation.Remove(jsonPointer)),
				"sort" => new JsonPatch(PatchOperation.Replace(jsonPointer, jsonDoc /* A sorted version! */)),
				_ => new Error<string>("Invalid Operation"),
			};

			if(operation.IsT1)
			{
				return new CallState("#-1 INVALID OPERATION");
			}

			var patchResult = operation.AsT0.Apply(jsonDoc);
			
			return patchResult.IsSuccess 
				? new CallState(patchResult.Result!.ToString()) 
				: new CallState($"#-1 PATCH FAILED: {patchResult.Error}");
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

		var isWizard = executor.IsGod() || await executor.IsWizard();
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

				await Mediator!.Publish(new SignalGMCPNotification(
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