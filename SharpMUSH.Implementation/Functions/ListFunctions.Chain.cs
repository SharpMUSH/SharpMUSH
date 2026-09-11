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
	[SharpFunction(Name = "chain", MinArgs = 2, MaxArgs = 32, Flags = FunctionFlags.Regular, ParameterNames = ["attributes", "base", "arguments..."])]
	public async ValueTask<CallState> Chain(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var attrListStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var tokens = attrListStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		// The base is the value threaded through the pipeline; it becomes %0 for the first attribute.
		var accumulator = parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty;

		if (tokens.Length == 0)
		{
			return errors.Complete(new CallState(accumulator));
		}

		// Fixed side-arguments: chain(<list>, <base>, <arg0>, <arg1>, ...) exposes <arg0> as %1, <arg1> as
		// %2, ... to EVERY attribute in the chain (carried down each step, as in PennMUSH's chain()).
		var sideArgs = new Dictionary<string, CallState>();
		for (var i = 2; i < parser.CurrentState.Arguments.Count; i++)
		{
			if (parser.CurrentState.Arguments.TryGetValue(i.ToString(), out var sideArg))
			{
				sideArgs[(i - 1).ToString()] = sideArg;
			}
		}

		// Push an iteration context so a step can short-circuit the pipeline with ibreak(), exactly as it
		// would inside iter()/map(). itext(0)/inum(0) inside a step then see the running value and step.
		var wrappedIteration = new IterationWrapper<MString>
		{ Value = accumulator, Break = false, NoBreak = false, Iteration = 0 };
		parser.CurrentState.IterationRegisters.Push(wrappedIteration);

		try
		{
			foreach (var token in tokens)
			{
				if (!(await AttributeService.FetchAttributeFunctionAsync(parser, executor, token)).TryGetValue(out var function, out var refusal))
				{
					return errors.Complete(refusal);
				}

				wrappedIteration.Value = accumulator;
				wrappedIteration.Iteration++;

				// %0 is the running value threaded from the previous step; %1, %2, ... are the side-arguments.
				var env = new Dictionary<string, CallState>(sideArgs) { ["0"] = new CallState(accumulator) };

				var stepParser = parser.Push(parser.CurrentState with
				{
					Arguments = new Dictionary<string, CallState>(env),
					EnvironmentRegisters = env
				});

				accumulator = errors.Record(await AttributeService.CallAttributeFunctionAsync(stepParser, function));

				// A step called ibreak(): stop the pipeline and return the value produced so far.
				if (wrappedIteration.Break)
				{
					break;
				}
			}
		}
		finally
		{
			parser.CurrentState.IterationRegisters.TryPop(out _);
		}

		return errors.Complete(new CallState(accumulator));
	}
}
