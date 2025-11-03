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
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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


	[SharpFunction(Name = "isjson", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
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
	public static async ValueTask<CallState> json_map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Arg0: Object/Attribute
		// Arg1: JSON string
		// Arg2: Output separator (optional, defaults to space)
		// Arg3+: Additional arguments passed as %3, %4, etc.

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

		// Parse additional user arguments (available as %3, %4, etc.)
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
					// For basic types, call attribute once
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
					// For arrays, call once per element with %2 as the index
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
					// For objects, call once per key/value pair with %2 as the key
					foreach (var property in rootElement.EnumerateObject())
					{
						var objArgs = new Dictionary<string, CallState>
						{
							{ "0", new CallState(JsonHelpers.GetJsonType(property.Value)) },
							{ "1", new CallState(property.Value.GetRawText()) },
							{ "2", new CallState(property.Name) }
						};
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
			}

			return new CallState(MModule.multipleWithDelimiter(osep, result));
		}
		catch (JsonException)
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "json_map"));
		}
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

	[SharpFunction(Name = "oob", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> oob(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known;

		// Arg0: Players (space-separated list of player names/dbrefs)
		// Arg1: Package name
		// Arg2: JSON message (optional)
		
		var playersArg = MModule.plainText(parser.CurrentState.Arguments["0"].Message!);
		var package = MModule.plainText(parser.CurrentState.Arguments["1"].Message!);
		var message = parser.CurrentState.Arguments.TryGetValue("2", out var msgState) 
			? msgState.Message?.ToString() ?? ""
			: "";

		// Parse players list using NameListString helper
		var players = ArgHelpers.NameListString(playersArg);

		// Validate message is valid JSON if provided
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

		// Check permissions once: must be wizard, have Send_OOB power, or will check per-player for self
		bool isWizard = executor.IsGod() || await executor.IsWizard();
		// TODO: Check for Send_OOB power when powers are implemented
		bool hasSendOOBPower = false;

		int sentCount = 0;

		// Locate each player and send message to their connections
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

			// Check if located object is a player
			if (!located.IsPlayer)
			{
				continue;
			}

			// Check permissions: must be wizard, have Send_OOB power, or sending to self
			bool isSelf = executor.Object().DBRef == located.Object().DBRef;

			if (!isWizard && !isSelf && !hasSendOOBPower)
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			// Get all connections for this player
			await foreach (var connection in ConnectionService!.Get(located.Object().DBRef))
			{
				// Check if connection supports GMCP
				if (connection.Metadata.GetValueOrDefault("GMCP", "0") != "1")
				{
					continue;
				}

				// Send GMCP message
				await Mediator!.Publish(new SignalGMCPNotification(
					connection.Handle,
					package,
					message));
				
				sentCount++;
			}
		}

		return new CallState(sentCount.ToString());
	}
}