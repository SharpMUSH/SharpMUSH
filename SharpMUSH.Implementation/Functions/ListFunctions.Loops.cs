using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// The loops RhostMUSH has beside iter(): <c>nsiter()</c>, <c>citer()</c>, <c>inf()</c> and
/// <c>while()</c>.
/// </summary>
public partial class Functions
{
	/// <summary><c>nsiter(&lt;list&gt;, &lt;pattern&gt;[, &lt;delimiter&gt;])</c>: iter() with nothing between the results.</summary>
	[SharpFunction(Name = "nsiter", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse, ParameterNames = ["list", "pattern", "delimiter"])]
	public async ValueTask<CallState> NoSpaceIter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var listArg = errors.Record(await parser.CurrentState.Arguments["0"].GetParsedResultAsync());
		var delimiter = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);

		var results = await RunPatternLoopAsync(parser, MushText.SplitList(delimiter, listArg), errors, bound: null);
		return errors.Complete(new CallState(MarkupText.Concat(results)));
	}

	/// <summary>
	/// <c>citer(&lt;string&gt;, &lt;pattern&gt;[, &lt;output separator&gt;])</c>: iter() over the characters of a
	/// string, spaces included.
	/// </summary>
	[SharpFunction(Name = "citer", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.NoParse, ParameterNames = ["string", "pattern", "output-separator"])]
	public async ValueTask<CallState> CharacterIter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var text = errors.Record(await parser.CurrentState.Arguments["0"].GetParsedResultAsync());
		var separator = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);

		var results = await RunPatternLoopAsync(parser, text.EnumerateGraphemes(), errors, bound: null);
		return errors.Complete(new CallState(MarkupText.Join(separator, results)));
	}

	/// <summary>
	/// <c>inf(&lt;list&gt;, &lt;pattern&gt;[, &lt;delimiter&gt;[, &lt;output separator&gt;]])</c>: iter(), and
	/// for a list written <c>:&lt;start&gt;:&gt;:</c> or <c>:&lt;start&gt;:&lt;:</c>, a count up or down from
	/// <c>&lt;start&gt;</c> that goes on until the pattern calls ibreak(). The output separator defaults to
	/// a space, as iter()'s does. An endless count still stops at
	/// the game's function invocation limit, at the output limit, or when the time limit runs out.
	/// </summary>
	[SharpFunction(Name = "inf", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.NoParse, ParameterNames = ["list", "pattern", "delimiter", "output-separator"])]
	public async ValueTask<CallState> InfiniteIter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var listArg = errors.Record(await parser.CurrentState.Arguments["0"].GetParsedResultAsync());
		var delimiter = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);
		var separator = await errors.OutputSeparatorAsync(parser, 3, MarkupText.Space);

		var results = ReadCount(listArg.ToPlainText()) is { } count
			? await RunPatternLoopAsync(parser, Count(count.Start, count.Step), errors,
				new LoopBound(checked((int)Configuration.CurrentValue.Limit.FunctionInvocationLimit), parser.CurrentState.OutputLimit, separator.Length))
			: await RunPatternLoopAsync(parser, MushText.SplitList(delimiter, listArg), errors, bound: null);

		return errors.Complete(new CallState(MarkupText.Join(separator, results)));

		static IEnumerable<MString> Count(long start, int step)
		{
			for (var value = start; ; value += step)
			{
				yield return MarkupText.Plain(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
		}
	}

	/// <summary>
	/// RhostMUSH's endless list: a first colon, a whole number after it, and the text from the second
	/// colon on exactly <c>:&gt;:</c> (count up) or <c>:&lt;:</c> (count down). Anything else is an
	/// ordinary list.
	/// </summary>
	private static (long Start, int Step)? ReadCount(string list)
	{
		var first = list.IndexOf(':');
		if (first < 0) return null;
		var second = list.IndexOf(':', first + 1);
		if (second < 0) return null;

		var step = list.AsSpan(second) switch
		{
			":>:" => 1,
			":<:" => -1,
			_ => 0
		};

		return step == 0 ? null : (RhostNumbers.LeadingInteger(list.AsSpan(first + 1, second - first - 1)), step);
	}

	/// <summary>
	/// How far an endless loop may go: <see cref="Iterations"/> passes, and results whose joined length
	/// stays within <see cref="OutputLimit"/>.
	/// </summary>
	private readonly record struct LoopBound(int Iterations, int OutputLimit, int SeparatorLength);

	/// <summary>
	/// Evaluates the pattern (argument 1) once for each item, as iter() does: ## and #@ read the item and
	/// its position, %i0 and inum(0) do too, and ibreak() ends the loop after the pass that calls it. A
	/// limit tripped inside the pattern, or the time limit, ends it as well.
	/// </summary>
	private async ValueTask<List<MString>> RunPatternLoopAsync(IMUSHCodeParser parser, IEnumerable<MString> items,
		ListEvaluationErrors errors, LoopBound? bound)
	{
		var frame = new IterationWrapper<MString>
		{
			Value = MarkupText.Empty, Break = false, NoBreak = false, Iteration = 0,
			Spliced = true, Evaluation = parser.CurrentState.CurrentEvaluation
		};
		var pattern = parser.CurrentState.Arguments["1"];
		var limitExceeded = parser.CurrentState.LimitExceeded;
		var results = new List<MString>();
		var length = 0;

		parser.CurrentState.IterationRegisters.Push(frame);
		try
		{
			foreach (var item in items)
			{
				ExecutionBudget.Current?.ThrowIfExceeded();
				if (bound is { } limits && frame.Iteration >= limits.Iterations) break;

				frame.Value = item;
				frame.Iteration++;
				var result = errors.Record(await pattern.GetParsedResultAsync());

				if (bound is { } output)
				{
					length += result.Length + (results.Count > 0 ? output.SeparatorLength : 0);
					if (length > output.OutputLimit) break;
				}

				results.Add(result);
				if (frame.Break || limitExceeded?.IsExceeded == true) break;
			}
		}
		finally
		{
			parser.CurrentState.IterationRegisters.TryPop(out _);
		}

		return results;
	}

	/// <summary>One side of while(): what to run on an item, and the code it runs, to tell whether the two sides are the same.</summary>
	private sealed record WhileStep(string Code, Func<MString, ValueTask<CallState>> Run);

	private union WhileStepOrRefusal(WhileStep, CallState);

	/// <summary>
	/// <c>while(&lt;eval&gt;, &lt;condition&gt;, &lt;list&gt;, &lt;stop&gt;[, &lt;delimiter&gt;[, &lt;output separator&gt;]])</c>:
	/// map() over <c>&lt;list&gt;</c> that stops after the item for which <c>&lt;condition&gt;</c> gives exactly
	/// <c>&lt;stop&gt;</c>. That item's result is kept. When both attributes hold the same code it runs once
	/// per item. A missing attribute gives nothing.
	/// </summary>
	[SharpFunction(Name = "while", MinArgs = 4, MaxArgs = 6, Flags = FunctionFlags.Regular, ParameterNames = ["eval-attribute", "condition-attribute", "list", "stop-value", "delimiter", "output-separator"])]
	public async ValueTask<CallState> While(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delimiter = OptionalArgument(args, 4, MarkupText.Space);
		var separator = OptionalArgument(args, 5, delimiter);

		return await ResolveWhileStepAsync(parser, executor, args["0"].Message) switch
		{
			WhileStep evaluate => await ResolveWhileStepAsync(parser, executor, args["1"].Message) switch
			{
				WhileStep condition => await WhileLoopAsync(parser, evaluate, condition,
					MushText.SplitList(delimiter, args["2"].Message), args["3"].Message.ToPlainText(), separator),
				CallState refusal => refusal
			},
			CallState refusal => refusal
		};
	}

	private async ValueTask<CallState> WhileLoopAsync(IMUSHCodeParser parser, WhileStep evaluate, WhileStep condition,
		MString[] list, string stop, MString separator)
	{
		var errors = new ListEvaluationErrors();
		var sameCode = evaluate.Code == condition.Code;
		var limitExceeded = parser.CurrentState.LimitExceeded;
		var results = new List<MString>();

		foreach (var item in list)
		{
			ExecutionBudget.Current?.ThrowIfExceeded();

			var result = errors.Record(await evaluate.Run(item));
			results.Add(result);

			var test = sameCode ? result : errors.Record(await condition.Run(item));
			if (test.ToPlainText() == stop || limitExceeded?.IsExceeded == true) break;
		}

		return errors.Complete(new CallState(MarkupText.Join(separator, results)));
	}

	/// <summary>
	/// One side of while(), read the way map() reads its attribute: a #lambda or #apply, or an attribute
	/// fetched with the same permission checks. A missing attribute is a refusal that gives nothing.
	/// </summary>
	private async ValueTask<WhileStepOrRefusal> ResolveWhileStepAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString specifier)
	{
		var text = specifier.ToPlainText();
		if (HelperFunctions.IsLambdaOrApply(text))
		{
			return new WhileStep(text, item => AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor,
				specifier, new Dictionary<string, CallState> { ["0"] = new(item) }));
		}

		return await AttributeService.FetchAttributeFunctionAsync(parser, executor, text) switch
		{
			AttributeFunction function => new WhileStep(function.Code.ToString(), item => CallAttributeWithArgumentsAsync(parser,
				function, new Dictionary<string, CallState> { ["0"] = new(item) })),
			CallState refusal when refusal.Message.ToPlainText() == ErrorMessages.Returns.NoSuchAttribute => CallState.Empty,
			CallState refusal => refusal
		};
	}

	/// <summary>Argument <paramref name="index"/>, or <paramref name="fallback"/> when it is missing or empty.</summary>
	private static MString OptionalArgument(OrderedArguments args, int index, MString fallback)
		=> args.Count > index && args[index.ToString()].Message is { Length: > 0 } value ? value : fallback;
}
