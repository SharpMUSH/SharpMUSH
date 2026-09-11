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

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "jiter", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["attributes", "input", "osep"])]
	public async ValueTask<CallState> JuxtaposedIteration(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var attrListStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var tokens = attrListStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		// jiter fans ONE input across every attribute: each is evaluated with the same %0,
		// side by side (contrast chain(), which threads each result into the next step).
		var input = parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty;
		var osep = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);

		if (tokens.Length == 0)
		{
			return errors.Complete(CallState.Empty);
		}

		var results = new List<MString>(tokens.Length);

		foreach (var token in tokens)
		{
			if (!(await AttributeService.FetchAttributeFunctionAsync(parser, executor, token)).TryGetValue(out var function, out var refusal))
			{
				return errors.Complete(refusal);
			}

			var env = new Dictionary<string, CallState> { ["0"] = new CallState(input) };

			var stepParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState>(env),
				EnvironmentRegisters = env
			});

			results.Add(errors.Record(await AttributeService.CallAttributeFunctionAsync(stepParser, function)));
		}

		return errors.Complete(new CallState(MarkupText.Join(osep, results)));
	}
}
