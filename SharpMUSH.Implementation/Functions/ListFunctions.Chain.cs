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
	public static async ValueTask<CallState> Chain(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var attrListStr = MModule.plainText(parser.CurrentState.Arguments["0"].Message!)!;
		var tokens = attrListStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		// The base is the value threaded through the pipeline; it becomes %0 for the first attribute.
		var accumulator = parser.CurrentState.Arguments["1"].Message ?? MModule.empty();

		if (tokens.Length == 0)
		{
			return new CallState(accumulator);
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
			foreach (var objAttr in tokens.Select(HelperFunctions.SplitOptionalObjectAndAttr))
			{
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

				wrappedIteration.Value = accumulator;
				wrappedIteration.Iteration++;

				// %0 is the running value threaded from the previous step; %1, %2, ... are the side-arguments.
				var env = new Dictionary<string, CallState>(sideArgs) { ["0"] = new CallState(accumulator) };

				var stepParser = parser.Push(parser.CurrentState with
				{
					Arguments = new Dictionary<string, CallState>(env),
					EnvironmentRegisters = env
				});

				accumulator = (await stepParser.FunctionParse(attrValue))!.Message!;

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

		return new CallState(accumulator);
	}
}
