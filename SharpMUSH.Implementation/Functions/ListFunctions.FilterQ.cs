using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// filter() plus reject-capture. The register comes FIRST (setq()/setr() convention) because
	/// filter()'s argument positions 4+ carry PennMUSH-compatible extra predicate arguments
	/// (%1, %2, ...), so a register could not be appended without breaking ported softcode.
	/// </summary>
	[SharpFunction(Name = "filterq", MinArgs = 3, MaxArgs = 36, Flags = FunctionFlags.Regular, ParameterNames = ["register", "attribute", "list", "delimiter", "osep"])]
	public async ValueTask<CallState> FilterQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var registerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		if (string.IsNullOrWhiteSpace(registerName))
		{
			return errors.Complete(new CallState(ErrorMessages.Returns.BadRegName));
		}

		var rawAttrArg = parser.CurrentState.Arguments["1"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var delim = await errors.DefaultArgumentAsync(parser, 3, MarkupText.Space);
		var sep = await errors.DefaultArgumentAsync(parser, 4, delim);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["2"].Message!);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var lambdaResults = await EvaluateLambdaOrApplyForEachItemAsync(parser, executor, rawAttrArg, list, errors);
			return FilteredCapturingRejects(parser, registerName, sep, list, lambdaResults, errors);
		}

		// Extra args (positions 5+) reach the predicate as %1, %2, ... — filter()'s positions
		// 4+ shifted one to the right by the leading register.
		return await AttributeService.FetchAttributeFunctionAsync(parser, executor, rawAttrStr) switch
		{
			AttributeFunction function => FilteredCapturingRejects(parser, registerName, sep, list,
				await CallAttributeForEachItemAsync(parser, function, list, errors, ExtraPredicateArguments(parser, 5)), errors),
			CallState refusal => errors.Complete(refusal),
		};
	}

	/// <summary>
	/// filterq()'s answer: the items whose predicate result is 1, with the rest stored in
	/// <paramref name="registerName"/>, both joined by <paramref name="sep"/>.
	/// </summary>
	private static CallState FilteredCapturingRejects(IMUSHCodeParser parser, string registerName, MString sep,
		MString[] list, List<MString> results, ListEvaluationErrors errors)
	{
		var matches = new List<MString>();
		var rejects = new List<MString>();

		foreach (var (item, result) in list.Zip(results, (item, result) => (item, result)))
		{
			if (result.ToPlainText() == "1")
			{
				matches.Add(item);
			}
			else
			{
				rejects.Add(item);
			}
		}

		if (!parser.CurrentState.AddRegister(registerName.ToUpper(), MarkupText.Join(sep, rejects)))
		{
			return errors.Complete(new CallState(ErrorMessages.Returns.BadRegName));
		}

		return errors.Complete(new CallState(MarkupText.Join(sep, matches)));
	}
}
