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
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "json_group_by", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter"])]
	public async ValueTask<CallState> JsonGroupBy(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var delim = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);

		// A blank list has nothing to group.
		if (list.Length == 0 || (list.Length == 1 && string.IsNullOrEmpty(list[0].ToPlainText())))
		{
			return errors.Complete(new CallState("{}"));
		}

		// Keys appear in first-seen order (JsonObject preserves insertion order); each key maps to
		// a JSON array of the original elements that produced it.
		var groups = new JsonObject();

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var lambdaResults = await EvaluateLambdaOrApplyForEachItemAsync(parser, executor, rawAttrArg, list, errors);
			foreach (var (item, keyResult) in list.Zip(lambdaResults, (item, keyResult) => (item, keyResult)))
			{
				AddToJsonGroup(groups, keyResult.ToPlainText(), item.ToPlainText());
			}

			return errors.Complete(new CallState(groups.ToJsonString(JsonHelpers.RelaxedJsonOptions)));
		}

		if (!(await AttributeService.FetchAttributeFunctionAsync(parser, executor, rawAttrStr)).TryGetValue(out var function, out var refusal))
		{
			return errors.Complete(refusal);
		}

		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
				EnvironmentRegisters = new Dictionary<string, CallState> { ["0"] = new CallState(item) }
			});

			var key = errors.Record(await AttributeService.CallAttributeFunctionAsync(newParser, function)).ToPlainText();
			AddToJsonGroup(groups, key, item.ToPlainText());
		}

		return errors.Complete(new CallState(groups.ToJsonString(JsonHelpers.RelaxedJsonOptions)));
	}

	private void AddToJsonGroup(JsonObject groups, string key, string element)
	{
		if (groups[key] is not JsonArray bucket)
		{
			bucket = [];
			groups[key] = bucket;
		}

		bucket.Add(JsonValue.Create(element));
	}
}
