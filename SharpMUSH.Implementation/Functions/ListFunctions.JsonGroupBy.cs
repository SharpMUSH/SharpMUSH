using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json.Nodes;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "json_group_by", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter"])]
	public static async ValueTask<CallState> JsonGroupBy(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = MModule.plainText(rawAttrArg)!;

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var list = MModule.splitList(delim, parser.CurrentState.Arguments["1"].Message!);

		// A blank list has nothing to group.
		if (list.Length == 0 || (list.Length == 1 && string.IsNullOrEmpty(MModule.plainText(list[0]))))
		{
			return new CallState("{}");
		}

		// Keys appear in first-seen order (JsonObject preserves insertion order); each key maps to
		// a JSON array of the original elements that produced it.
		var groups = new JsonObject();

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var lambdaResults = await EvaluateLambdaOrApplyForEachItemAsync(parser, executor, rawAttrArg, list);
			foreach (var (item, keyResult) in list.Zip(lambdaResults, (item, keyResult) => (item, keyResult)))
			{
				AddToJsonGroup(groups, keyResult.ToPlainText(), MModule.plainText(item));
			}

			return new CallState(groups.ToJsonString(JsonHelpers.RelaxedJsonOptions));
		}

		var objAttr = HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, executor, dbref, LocateFlags.All);
		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService!.GetAttributeAsync(
			executor, located, attrName, mode: IAttributeService.AttributeMode.Execute, parent: true);
		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attrValue = maybeAttr.AsAttribute.Last().Value;

		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
				EnvironmentRegisters = new Dictionary<string, CallState> { ["0"] = new CallState(item) }
			});

			var key = (await newParser.FunctionParse(attrValue))!.Message!.ToPlainText();
			AddToJsonGroup(groups, key, MModule.plainText(item));
		}

		return new CallState(groups.ToJsonString(JsonHelpers.RelaxedJsonOptions));
	}

	private static void AddToJsonGroup(JsonObject groups, string key, string element)
	{
		if (groups[key] is not JsonArray bucket)
		{
			bucket = [];
			groups[key] = bucket;
		}

		bucket.Add(JsonValue.Create(element));
	}
}
