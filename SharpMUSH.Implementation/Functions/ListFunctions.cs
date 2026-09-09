using SharpMUSH.Library.Markup;
using MoreLinq.Extensions;
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
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "elements", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "positions", "delimiter"])]
	public ValueTask<CallState> Elements(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var positionsArg = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		var sep = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);

		// fun_elements (src/funlist.c): the positions are answered in the order they were asked for,
		// 1-based with a negative counting from the end, and one that names no element is skipped.
		var picked = new List<MString>();
		var positions = positionsArg.AsSpan();
		foreach (var range in positions.Split(' '))
		{
			if (TryListPosition(positions[range], list.Length, out var index))
			{
				picked.Add(list[index]);
			}
		}

		return ValueTask.FromResult<CallState>(MarkupText.Join(sep, picked));
	}

	/// <summary>
	/// PennMUSH's <c>find_list_position</c> without <c>insert</c>: a 1-based position, or a negative one
	/// counting back from the end, as a 0-based index into a list of <paramref name="total"/> items.
	/// </summary>
	private static bool TryListPosition(ReadOnlySpan<char> text, int total, out int index)
	{
		index = -1;
		if (!int.TryParse(text, out var position)) return false;
		if (position < 0) position = total + 1 + position;
		if (position < 1 || position > total) return false;
		index = position - 1;
		return true;
	}

	/// <summary>The 0-based indexes named by a space-separated list of positions, see <see cref="TryListPosition"/>.</summary>
	private static HashSet<int> ListPositions(string positionsArg, int total)
	{
		var indexes = new HashSet<int>();
		var positions = positionsArg.AsSpan();
		foreach (var range in positions.Split(' '))
		{
			if (TryListPosition(positions[range], total, out var index))
			{
				indexes.Add(index);
			}
		}

		return indexes;
	}

	[SharpFunction(Name = "elist", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list", "conjunction", "delim", "osep", "punctuation"])]
	public ValueTask<CallState> SeperatedList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var space = MarkupText.Space;
		var list = parser.CurrentState.ArgumentsOrdered["0"].Message!;
		var conjunction = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "and");
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, space);
		var outSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delim);
		var punctuation = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, ",");
		var splitList = MushText.SplitList(delim, list);

		if (splitList.Length == 2)
		{
			return ValueTask.FromResult<CallState>(
				MarkupText.Join(outSeparator, [splitList[0], MarkupText.Concat(conjunction, MarkupText.Concat(outSeparator, splitList[1]))]));
		}

		if (splitList.Length > 2)
		{
			splitList[^1] = MarkupText.Concat(conjunction, MarkupText.Concat(outSeparator, splitList[^1]));
		}

		return ValueTask.FromResult<CallState>(
			MarkupText.Join(MarkupText.Concat(punctuation, outSeparator), splitList));
	}

	[SharpFunction(Name = "extract", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "start", "length", "delimiter"])]
	public async ValueTask<CallState> Extract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var first = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MushText.One).ToPlainText();
		var length = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MushText.One).ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Space);

		if (!int.TryParse(first, out var firstNumber))
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "FIRST (arg 2)"));
		}

		if (!int.TryParse(length, out var lengthNumber))
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "LENGTH (arg 3)"));
		}

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		var range = firstNumber > 0
			? list.Skip(firstNumber - 1)
			: Enumerable.TakeLast(list, Math.Abs(firstNumber));
		var result = lengthNumber > 0
			? range.Take(lengthNumber)
			: Enumerable.TakeLast(range, Math.Abs(lengthNumber));

		return new CallState(MarkupText.Join(delimiter, result));
	}

	[SharpFunction(Name = "filter", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter"])]
	public async ValueTask<CallState> Filter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var lambdaResults = await EvaluateLambdaOrApplyForEachItemAsync(parser, executor, rawAttrArg, list);
			var filteredItems = list.Zip(lambdaResults, (item, boolResult) => (item, boolResult))
				.Where(pair => pair.boolResult.ToPlainText() == "1")
				.Select(pair => pair.item);
			return new CallState(MarkupText.Join(sep, filteredItems));
		}

		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;

		var environmentRegisters = new Dictionary<string, CallState>();
		for (var i = 4; i < parser.CurrentState.ArgumentsOrdered.Count; i++)
		{
			environmentRegisters[(i - 3).ToString()] = parser.CurrentState.ArgumentsOrdered[i.ToString()];
		}

		var result = new List<MString>();
		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
				EnvironmentRegisters = new Dictionary<string, CallState>(environmentRegisters)
				{
					["0"] = new CallState(item)
				}
			});

			if ((await newParser.FunctionParse(attrValue))!.Message!.ToPlainText() == "1")
			{
				result.Add(item);
			}
		}

		return new CallState(MarkupText.Join(sep, result));
	}

	// (attribute, list, delimiter, outsep) — arg 0 is the boolean predicate attribute, read below
	// as Arguments["0"]; matches the helpfile filterbool([<obj>]/<attr>, <list>[, <delim>[, <osep>]]).
	// These names drive the LSP's inlay hints, so a wrong order mislabels real code.
	[SharpFunction(Name = "filterbool", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "outsep"])]
	public async ValueTask<CallState> FilterBool(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var lambdaResults = await EvaluateLambdaOrApplyForEachItemAsync(parser, executor, rawAttrArg, list);
			var filteredItems = list.Zip(lambdaResults, (item, boolResult) => (item, boolResult))
				.Where(pair => pair.boolResult.Truthy(parser))
				.Select(pair => pair.item);

			return new CallState(MarkupText.Join(sep, filteredItems));
		}

		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;

		var environmentRegisters = new Dictionary<string, CallState>();
		for (var i = 4; i < parser.CurrentState.ArgumentsOrdered.Count; i++)
		{
			environmentRegisters[(i - 3).ToString()] = parser.CurrentState.ArgumentsOrdered[i.ToString()];
		}

		var result = new List<MString>();
		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
				EnvironmentRegisters = new Dictionary<string, CallState>(environmentRegisters)
				{
					["0"] = new CallState(item)
				}
			});

			if ((await newParser.FunctionParse(attrValue))!.Message!.Truthy(parser))
			{
				result.Add(item);
			}
		}

		return new CallState(MarkupText.Join(sep, result));
	}

	[SharpFunction(Name = "first", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> FirstInList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Space);
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MushText.SplitList(delim, listArg ?? MarkupText.Empty);
		var first = list.FirstOrDefault() ?? MarkupText.Empty;

		return ValueTask.FromResult(new CallState(first));
	}

	[SharpFunction(Name = "firstof", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object..."])]
	public async ValueTask<CallState> FirstOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		// PennMUSH: evaluate each arg in order, return the first truthy one.
		// If none are truthy, return the value of the last arg.
		CallState lastValue = CallState.Empty;
		foreach (var arg in args)
		{
			var parsed = await arg.Value.ParsedMessage();
			lastValue = parsed;
			if (parsed.Truthy(parser))
			{
				return parsed;
			}
		}

		return lastValue;
	}

	// Argument order is (attr, list, base, delimiter) — matching the helpfile and the indices read
	// below. These names drive the LSP's inlay hints, so a wrong order mislabels real code.
	[SharpFunction(Name = "fold", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "base", "delimiter"])]
	public async ValueTask<CallState> Fold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var baseCase = parser.CurrentState.ArgumentsOrdered.TryGetValue("2", out var baseCaseArg)
			? baseCaseArg.Message
			: null;
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MarkupText.Space);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);

		if (list.Length == 0)
		{
			// Folding nothing into a base case is the base case — the identity every reduce
			// shares (Haskell `foldl f z [] = z`, Python `reduce(f, [], init)`). The base is the
			// answer when there is nothing to combine into it, not merely a seed for a first
			// call, so returning empty here loses the caller's own value. With no base there is
			// genuinely nothing to return.
			// PennMUSH's fun_fold has no empty-list guard: it calls <attr> once with %1 read
			// from an unset slot. That is a defect, not a contract, and is not reproduced.
			return baseCase is null ? CallState.Empty : new CallState(baseCase);
		}

		MString accumulator;
		var startIndex = 0;
		var iteration = 0;

		if (baseCase != null)
		{
			accumulator = baseCase;
		}
		else
		{
			if (list.Length < 2)
			{
				return new CallState(list[0]);
			}
			accumulator = list[0];
			startIndex = 1;
		}

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			for (var i = startIndex; i < list.Length; i++)
			{
				accumulator = await AttributeService.EvaluateAttributeFunctionAsync(
					parser,
					executor,
					rawAttrArg,
					new Dictionary<string, CallState>
					{
						{ "0", new CallState(accumulator) },
						{ "1", new CallState(list[i]) },
						{ "2", new CallState(iteration) }
					});
				iteration++;
			}
			return new CallState(accumulator);
		}

		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;

		for (var i = startIndex; i < list.Length; i++)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState>
				{
					{ "0", new CallState(accumulator) },
					{ "1", new CallState(list[i]) },
					{ "2", new CallState(iteration) }
				},
				EnvironmentRegisters = new Dictionary<string, CallState>
				{
					["0"] = new CallState(accumulator),
					["1"] = new CallState(list[i]),
					["2"] = new CallState(iteration)
				}
			});
			accumulator = (await newParser.FunctionParse(attrValue))!.Message!;
			iteration++;
		}

		return new CallState(accumulator);
	}

	/// <summary>
	/// What a list function answers when the player's glob could not finish inside
	/// <see cref="SoftcodeRegex.MatchTimeout"/>. Not an empty list: these are softcode functions, and
	/// "no element matched" and "your pattern could not be evaluated" are different answers.
	/// </summary>
	private static ValueTask<CallState> TimedOut
		=> ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpTimeout));

	[SharpFunction(Name = "grab", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> Grab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = (parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty).ToPlainText()!;
		var regex = SoftcodeRegex.Wildcard(globPattern);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var splitList = MushText.SplitList(delimiter, list ?? MarkupText.Empty);

		try
		{
			return ValueTask.FromResult<CallState>(splitList
				.FirstOrDefault(x => regex.IsMatch(x.ToPlainText())) ?? MarkupText.Empty);
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			return TimedOut;
		}
	}

	[SharpFunction(Name = "graball", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> GrabAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = (parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty).ToPlainText()!;
		var regex = SoftcodeRegex.Wildcard(globPattern);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, delimiter);
		var splitList = MushText.SplitList(delimiter, list ?? MarkupText.Empty);

		try
		{
			return ValueTask.FromResult<CallState>(
				MarkupText.Join(outputSep, splitList.Where(x => regex.IsMatch(x.ToPlainText()))));
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			return TimedOut;
		}
	}

	[SharpFunction(Name = "index", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "element", "delimiter"])]
	public ValueTask<CallState> Index(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var delimiter = args["1"].Message!;
		var firstArg = args["2"].Message!.ToPlainText();
		var lengthArg = args["3"].Message!.ToPlainText();

		if (!int.TryParse(firstArg, out var first))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		if (!int.TryParse(lengthArg, out var length))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		var range = first > 0
			? list.Skip(first - 1)
			: Enumerable.TakeLast(list, Math.Abs(first));
		var result = length > 0
			? range.Take(length)
			: Enumerable.TakeLast(range, Math.Abs(length));

		return ValueTask.FromResult(new CallState(MarkupText.Join(delimiter, result)));
	}

	[SharpFunction(Name = "iter", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.NoParse, ParameterNames = ["list", "pattern", "delimiter", "output-separator"])]
	public async ValueTask<CallState> Iter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var listArg = (await parser.CurrentState.Arguments["0"].ParsedMessage())!;

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MushText.SplitList(delim, listArg);
		var wrappedIteration = new IterationWrapper<MString>
		{ Value = MarkupText.Empty, Break = false, NoBreak = false, Iteration = 0 };
		var result = new List<MString>();

		// Replace ## with %iL in the pattern for PennMUSH backward compatibility
		var patternArg = parser.CurrentState.Arguments["1"];
		var modifiedPattern = patternArg.Message!.Text.Contains("##")
			? patternArg.Message.ReplaceAll("##", MarkupText.Plain("%iL"))
			: null;

		parser.CurrentState.IterationRegisters.Push(wrappedIteration);

		foreach (var item in list)
		{
			wrappedIteration.Value = item!;
			wrappedIteration.Iteration++;
			var parsed = modifiedPattern != null
				? (await parser.FunctionParse(modifiedPattern))?.Message
				: await patternArg.ParsedMessage();
			result.Add(parsed!);

			if (wrappedIteration.Break)
			{
				break;
			}
		}

		parser.CurrentState.IterationRegisters.TryPop(out _);

		return new CallState(MarkupText.Join(sep, result));
	}

	[SharpFunction(Name = "items", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> Items(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message!;
		var delimiter = parser.CurrentState.Arguments["1"].Message!;

		// items() counts the number of delimiter occurrences + 1; this naturally handles null items
		var listStr = list.ToPlainText();
		var delimStr = delimiter.ToPlainText();

		if (string.IsNullOrEmpty(delimStr))
		{
			return ValueTask.FromResult(new CallState(listStr.Length));
		}

		var count = 1;
		var index = 0;
		while ((index = listStr.IndexOf(delimStr, index, StringComparison.Ordinal)) != -1)
		{
			count++;
			index += delimStr.Length;
		}

		return ValueTask.FromResult(new CallState(count));
	}

	[SharpFunction(Name = "itemize", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter", "conjunction", "punctuation"])]
	public ValueTask<CallState> Itemize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var space = MarkupText.Space;
		var list = parser.CurrentState.ArgumentsOrdered["0"].Message!;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, space);
		var conjunction = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "and");
		var punctuation = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, ",");
		var splitList = MushText.SplitList(delim, list);

		if (splitList.Length == 2)
		{
			return ValueTask.FromResult<CallState>(
				MarkupText.Join(space, [splitList[0], MarkupText.Concat(conjunction, MarkupText.Concat(space, splitList[1]))]));
		}

		if (splitList.Length > 2)
		{
			splitList[^1] = MarkupText.Concat(conjunction, MarkupText.Concat(space, splitList[^1]));
		}

		return ValueTask.FromResult<CallState>(
			MarkupText.Join(MarkupText.Concat(punctuation, space), splitList));
	}

	[SharpFunction(Name = "ibreak", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["levels"])]
	public ValueTask<CallState> IterationBreak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, "1").ToPlainText();
		if (!long.TryParse(text, out var levels))
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		if (levels < 0 || levels > parser.CurrentState.IterationRegisters.Count)
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.OutOfRange));
		foreach (var iteration in parser.CurrentState.IterationRegisters.Take((int)levels))
			iteration.Break = true;
		return ValueTask.FromResult(CallState.Empty);
	}

	[SharpFunction(Name = "ilev", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public ValueTask<CallState> IterationLevel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var depth = parser.CurrentState.IterationRegisters.Count;
		return ValueTask.FromResult(new CallState(depth > 0 ? depth - 1 : -1));
	}

	[SharpFunction(Name = "inum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public ValueTask<CallState> IterationNumber(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var levelArg = args["0"].Message!.ToPlainText();
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (levelArg.Equals("L", StringComparison.OrdinalIgnoreCase))
		{
			// "L" refers to the outermost iteration
			if (maxCount == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
			}
			return ValueTask.FromResult(new CallState(parser.CurrentState.IterationRegisters.Last().Iteration));
		}

		if (!int.TryParse(levelArg, out var level))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		if (level < 0 || level >= maxCount)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
		}

		// The stack enumerates innermost-first, so the level IS the index: 0 = current, 1 = parent.
		var iteration = parser.CurrentState.IterationRegisters.ElementAt(level).Iteration;
		return ValueTask.FromResult(new CallState(iteration));
	}

	[SharpFunction(Name = "last", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> Last(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Space);
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MushText.SplitList(delim, listArg ?? MarkupText.Empty);
		var last = list.LastOrDefault() ?? MarkupText.Empty;

		return ValueTask.FromResult(new CallState(last));
	}

	[SharpFunction(Name = "ldelete", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "position", "delimiter"])]
	public ValueTask<CallState> ListDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var positionsArg = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		var deleted = ListPositions(positionsArg, list.Length);

		return ValueTask.FromResult<CallState>(
			MarkupText.Join(outputSep, list.Where((_, i) => !deleted.Contains(i))));
	}

	[SharpFunction(Name = "map", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "outsep"])]
	public async ValueTask<CallState> Map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var lambdaResults = await EvaluateLambdaOrApplyForEachItemAsync(parser, executor, rawAttrArg, list);
			return new CallState(MarkupText.Join(sep, lambdaResults));
		}

		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
		var objAttr = HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;

		var mapResult = new List<MString>(list.Length);
		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
				EnvironmentRegisters = new Dictionary<string, CallState> { { "0", new CallState(item) } }
			});
			mapResult.Add((await newParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MarkupText.Join(sep, mapResult));
	}

	[SharpFunction(Name = "match", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "pattern", "delimiter"])]
	public ValueTask<CallState> Match(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = (parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty).ToPlainText()!;
		var regex = SoftcodeRegex.Wildcard(globPattern);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var splitList = MushText.SplitList(delimiter, list ?? MarkupText.Empty);

		try
		{
			var index = splitList
				.Select((item, i) => (item, pos: i + 1))
				.FirstOrDefault(pair => regex.IsMatch(pair.item.ToPlainText()));

			return ValueTask.FromResult<CallState>(index.pos.ToString());
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			return TimedOut;
		}
	}

	[SharpFunction(Name = "matchall", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "pattern", "delimiter", "outsep"])]
	public ValueTask<CallState> MatchAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = (parser.CurrentState.Arguments["1"].Message ?? MarkupText.Empty).ToPlainText()!;
		var regex = SoftcodeRegex.Wildcard(globPattern);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, delimiter);
		var splitList = MushText.SplitList(delimiter, list ?? MarkupText.Empty);

		try
		{
			var positions = splitList
				.Select((item, i) => (item, pos: i + 1))
				.Where(pair => regex.IsMatch(pair.item.ToPlainText()))
				.Select(pair => MarkupText.Plain(pair.pos.ToString()));

			return ValueTask.FromResult<CallState>(MarkupText.Join(outputSep, positions));
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			return TimedOut;
		}
	}

	[SharpFunction(Name = "member", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.StripAnsi, ParameterNames = ["list", "element", "delimiter"])]
	public ValueTask<CallState> Member(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var list = args["0"].Message!;
		var word = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var index = Array.FindIndex(MushText.SplitList(delimiter, list), x => x.Text == word);

		return ValueTask.FromResult<CallState>(index + 1);
	}

	[SharpFunction(Name = "mix", MinArgs = 3, MaxArgs = 35, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list1", "list2", "delimiter", "outsep"])]
	public async ValueTask<CallState> Mix(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var argCount = parser.CurrentState.ArgumentsOrdered.Count;
		MString delimiter;
		var listCount = argCount - 1;

		// If more than 2 lists, last arg is delimiter
		if (argCount > 3)
		{
			delimiter = parser.CurrentState.ArgumentsOrdered[(argCount - 1).ToString()].Message!;
			listCount--;
		}
		else
		{
			delimiter = MarkupText.Space;
		}

		var lists = new List<MString[]>();
		var maxLength = 0;
		for (var i = 1; i <= listCount; i++)
		{
			var list = MushText.SplitList(delimiter, parser.CurrentState.ArgumentsOrdered[i.ToString()].Message!);
			lists.Add(list);
			maxLength = Math.Max(maxLength, list.Length);
		}

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var result = new List<MString>();
			for (var i = 0; i < maxLength; i++)
			{
				var args = new Dictionary<string, CallState>();
				for (var j = 0; j < lists.Count; j++)
				{
					args[j.ToString()] = new CallState(i < lists[j].Length ? lists[j][i] : MarkupText.Empty);
				}
				result.Add(await AttributeService.EvaluateAttributeFunctionAsync(parser, executor, rawAttrArg, args));
			}
			return new CallState(MarkupText.Join(delimiter, result));
		}

		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;

		var attrResult = new List<MString>();
		for (var i = 0; i < maxLength; i++)
		{
			var args = new Dictionary<string, CallState>();
			var envRegs = new Dictionary<string, CallState>();

			for (var j = 0; j < lists.Count; j++)
			{
				var value = i < lists[j].Length ? lists[j][i] : MarkupText.Empty;
				args[j.ToString()] = new CallState(value);
				envRegs[j.ToString()] = new CallState(value);
			}

			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = args,
				EnvironmentRegisters = envRegs
			});
			attrResult.Add((await newParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MarkupText.Join(delimiter, attrResult));
	}

	[SharpFunction(Name = "munge", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list1", "list2", "list3", "delimiter"])]
	public async ValueTask<CallState> Munge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 4, delim);

		var list1 = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);
		var list2 = MushText.SplitList(delim, parser.CurrentState.Arguments["2"].Message!);

		// Build args for the transformation call: %0 = whole list1, %1 = delimiter
		var mungeArgs = new Dictionary<string, CallState>
		{
			{ "0", new CallState(MarkupText.Join(delim, list1)) },
			{ "1", new CallState(delim) }
		};

		MString transformedList1Str;

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			transformedList1Str = await AttributeService.EvaluateAttributeFunctionAsync(
				parser, executor, rawAttrArg, mungeArgs);
		}
		else
		{
			var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
			var objAttr =
				HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
			if (objAttr is { IsT1: true, AsT1: false })
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			var (dbref, attrName) = objAttr.AsT0;
			dbref ??= executor.ToString();

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				dbref,
				LocateFlags.All);

			if (!locate.IsValid())
			{
				return CallState.Empty;
			}

			var located = locate.WithoutError().WithoutNone();

			var maybeAttr = await AttributeService.GetAttributeAsync(
				executor,
				located,
				attrName,
				mode: IAttributeService.AttributeMode.Execute,
				parent: true);

			if (maybeAttr.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchAttribute);
			}

			if (maybeAttr.IsError)
			{
				return new CallState(maybeAttr.AsError.Value);
			}

			var attr = maybeAttr.AsAttribute;
			var attrValue = attr.Last().Value;

			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = mungeArgs,
				EnvironmentRegisters = new Dictionary<string, CallState>(mungeArgs)
			});
			transformedList1Str = (await newParser.FunctionParse(attrValue))!.Message!;
		}

		var transformedList1 = MushText.SplitList(delim, transformedList1Str);

		// Create mapping from original list1 to list2
		var mapping = new Dictionary<string, MString>();
		for (var i = 0; i < Math.Min(list1.Length, list2.Length); i++)
		{
			mapping[list1[i].ToPlainText()] = list2[i];
		}

		// Rearrange list2 based on transformed list1
		var result = new List<MString>();
		foreach (var item in transformedList1)
		{
			if (mapping.TryGetValue(item.ToPlainText(), out var mappedValue))
			{
				result.Add(mappedValue);
			}
		}

		return new CallState(MarkupText.Join(sep, result));
	}

	/// <summary>One entry of a namegrab() list: the dbref as the caller spelled it, and the object's name.</summary>
	private sealed record NamedDbRef(string Token, string Name);

	/// <summary>
	/// The objects named by a delimited list of dbrefs, each looked up once, or <c>null</c> when an
	/// entry is not a dbref at all. An entry that names no object is skipped, as fun_namegrab skips
	/// garbage.
	/// </summary>
	private async ValueTask<List<NamedDbRef>?> NamedDbRefs(string list, string delimiter)
	{
		var named = new List<NamedDbRef>();
		foreach (var token in list.Split(delimiter, StringSplitOptions.RemoveEmptyEntries))
		{
			var dbref = HelperFunctions.ParseDbRef(token);
			if (dbref.IsNone())
			{
				return null;
			}

			var item = await Mediator.Send(new GetObjectNodeQuery(dbref.AsValue()));
			if (!item.IsNone)
			{
				named.Add(new NamedDbRef(token, item.Known.Object().Name));
			}
		}

		return named;
	}

	[SharpFunction(Name = "namegrab", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "pattern", "delimiter"])]
	public async ValueTask<CallState> NameGrab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ").ToPlainText();

		var named = await NamedDbRefs(args["0"].Message!.ToPlainText(), delimiter);
		if (named is null)
		{
			return "INVALID DBREF IN LIST";
		}

		// fun_namegrab: an exact name (strcasecmp) beats a partial one.
		var match = named.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
			?? named.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

		return match is null ? CallState.Empty : match.Token;
	}

	[SharpFunction(Name = "namegraball", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "pattern", "delimiter"])]
	public async ValueTask<CallState> NameGrabAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ").ToPlainText();

		var named = await NamedDbRefs(args["0"].Message!.ToPlainText(), delimiter);
		if (named is null)
		{
			return "INVALID DBREF IN LIST";
		}

		var exact = named.Where(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
		var matches = exact.Count > 0
			? exact
			: named.Where(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();

		return string.Join(" ", matches.Select(x => x.Token));
	}

	[SharpFunction(Name = "randextract", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list", "count", "delim", "type", "osep"])]
	public ValueTask<CallState> RandomExtract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var countArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MushText.One).ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		var typeArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Plain("R")).ToPlainText().ToUpper();
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		if (!int.TryParse(countArg, out var count))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		if (list.Length == 0)
		{
			return ValueTask.FromResult(CallState.Empty);
		}

		var random = new Random();
		IEnumerable<MString> result;

		if (typeArg == "L")
		{
			// Linear from random start
			var start = random.Next(list.Length);
			result = list.Skip(start).Take(count);
		}
		else if (typeArg == "D")
		{
			// Random with duplicates allowed
			result = Enumerable.Range(0, count).Select(_ => list[random.Next(list.Length)]);
		}
		else
		{
			// "R" or default: Random without duplicates
			result = list.OrderBy(_ => random.Next()).Take(count);
		}

		return ValueTask.FromResult<CallState>(MarkupText.Join(outputSep, result));
	}

	[SharpFunction(Name = "randword", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> RandomWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var list = orderedArgs["0"].Message!;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 1, " ");
		return ValueTask.FromResult<CallState>(
			MushText.SplitList(delimiter, list).RandomSubset(1).FirstOrDefault() ?? MarkupText.Empty);
	}

	[SharpFunction(Name = "remove", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "words", "delimiter"])]
	public ValueTask<CallState> Remove(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var list = parser.CurrentState.Arguments["0"].Message!;
		var words = parser.CurrentState.Arguments["1"].Message!;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 2, " ");

		var splitList = MushText.SplitList(delimiter, list).ToList();
		var splitWords = MushText.SplitList(delimiter, words);

		foreach (var word in splitWords)
		{
			var text = word.Text;
			var index = splitList.FindIndex(x => x.Text == text);
			if (index != -1)
			{
				splitList.RemoveAt(index);
			}
		}

		return ValueTask.FromResult<CallState>(MarkupText.Join(delimiter, splitList));
	}

	[SharpFunction(Name = "lreplace", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list", "positions", "new-item", "delimiter", "osep"])]
	public ValueTask<CallState> ListReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var positionsArg = args["1"].Message!.ToPlainText();
		var newItem = args["2"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Space);
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		foreach (var index in ListPositions(positionsArg, list.Length))
		{
			list[index] = newItem ?? MarkupText.Empty;
		}

		return ValueTask.FromResult<CallState>(MarkupText.Join(outputSep, list));
	}

	[SharpFunction(Name = "rest", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> Rest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, " ");
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty);

		return ValueTask.FromResult(new CallState(MarkupText.Join(delim, list.Skip(1))));
	}

	[SharpFunction(Name = "revwords", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public async ValueTask<CallState> ReverseList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, " ");
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MushText.SplitList(delim, (await parser.CurrentState.Arguments["0"].ParsedMessage())!);

		return new CallState(MarkupText.Join(sep, list.Reverse()));
	}

	[SharpFunction(Name = "shuffle", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> Shuffle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Space);
		var sep = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, delimiter);

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		var shuffled = ShuffleExtension.Shuffle(list);
		var result = MarkupText.Join(sep, shuffled);

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "sort", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "sort-type", "delimiter", "outsep"])]
	public async ValueTask<CallState> Sort(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var list = orderedArgs["0"].Message!;
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 1, MarkupText.Plain("")).ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 2, MarkupText.Space);
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 3, delimiter);
		var listItems = MushText.SplitList(delimiter, list);

		var sorted = SortService.Sort(listItems, (x, ct) => ValueTask.FromResult(x.ToPlainText()), parser,
			SortService.StringToSortType(sortType));

		return MarkupText.Join(outputSeparator, await sorted.ToArrayAsync());
	}

	[SharpFunction(Name = "sortby", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "outsep"])]
	public async ValueTask<CallState> SortBy(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);

		async Task<int> CompareViaLambda(MString a, MString b)
		{
			var result = await AttributeService.EvaluateAttributeFunctionAsync(
				parser,
				executor,
				rawAttrArg,
				new Dictionary<string, CallState>
				{
					{ "0", new CallState(a) },
					{ "1", new CallState(b) }
				});
			return int.TryParse(result.ToPlainText(), out var cmp) ? (cmp > 0 ? 1 : cmp < 0 ? -1 : 0) : 0;
		}

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			return new CallState(MarkupText.Join(sep, await SortByComparisons(list, CompareViaLambda)));
		}

		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;

		async Task<int> CompareViaAttribute(MString a, MString b)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState>
				{
					{ "0", new CallState(a) },
					{ "1", new CallState(b) }
				},
				EnvironmentRegisters = new Dictionary<string, CallState>
				{
					["0"] = new CallState(a),
					["1"] = new CallState(b)
				}
			});
			var result = (await newParser.FunctionParse(attrValue))!.Message!.ToPlainText();
			return int.TryParse(result, out var cmp) ? Math.Sign(cmp) : 0;
		}

		return new CallState(MarkupText.Join(sep, await SortByComparisons(list, CompareViaAttribute)));
	}

	/// <summary>
	/// fun_sortby's order: every item is compared against every other, its rank is the sum of those
	/// results, and equal ranks keep list order. One item's comparisons run as a unit, the items
	/// concurrently.
	/// </summary>
	private static async Task<IEnumerable<MString>> SortByComparisons(MString[] list,
		Func<MString, MString, Task<int>> compare)
	{
		var ranked = await Task.WhenAll(list.Select((item, index) => Task.Run(async () =>
		{
			var rank = 0;
			foreach (var other in list.Where((_, j) => j != index))
			{
				rank += await compare(item, other);
			}

			return (Item: item, Rank: rank);
		})));

		return ranked.OrderBy(r => r.Rank).Select(r => r.Item);
	}

	[SharpFunction(Name = "sortkey", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list", "attribute", "delimiter"])]
	public async ValueTask<CallState> SortKey(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var sortType = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MarkupText.Plain(""));
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 4, delim);

		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);
		var keys = new List<string>(list.Length);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			foreach (var item in list)
			{
				var keyResult = await AttributeService.EvaluateAttributeFunctionAsync(
					parser,
					executor,
					rawAttrArg,
					new Dictionary<string, CallState> { { "0", new CallState(item) } });
				keys.Add(keyResult.ToPlainText());
			}
		}
		else
		{
			var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
			var objAttr =
				HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
			if (objAttr is { IsT1: true, AsT1: false })
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			var (dbref, attrName) = objAttr.AsT0;
			dbref ??= executor.ToString();

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				dbref,
				LocateFlags.All);

			if (!locate.IsValid())
			{
				return CallState.Empty;
			}

			var located = locate.WithoutError().WithoutNone();

			var maybeAttr = await AttributeService.GetAttributeAsync(
				executor,
				located,
				attrName,
				mode: IAttributeService.AttributeMode.Execute,
				parent: true);

			if (maybeAttr.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchAttribute);
			}

			if (maybeAttr.IsError)
			{
				return new CallState(maybeAttr.AsError.Value);
			}

			var attr = maybeAttr.AsAttribute;
			var attrValue = attr.Last().Value;

			foreach (var item in list)
			{
				var newParser = parser.Push(parser.CurrentState with
				{
					Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
					EnvironmentRegisters = new Dictionary<string, CallState> { ["0"] = new CallState(item) }
				});
				keys.Add((await newParser.FunctionParse(attrValue))!.Message!.ToPlainText());
			}
		}

		var indexes = Enumerable.Range(0, list.Length);
		var sortedIndexes = sortType.ToPlainText().ToLower() switch
		{
			"n" => indexes.OrderBy(i => int.TryParse(keys[i], out var n) ? n : 0),
			"f" => indexes.OrderBy(i => double.TryParse(keys[i], out var f) ? f : 0.0),
			"i" => indexes.OrderBy(i => keys[i], StringComparer.OrdinalIgnoreCase),
			_ => indexes.OrderBy(i => keys[i])
		};

		return new CallState(MarkupText.Join(sep, sortedIndexes.Select(i => list[i])));
	}

	[SharpFunction(Name = "splice", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "word", "delimiter"])]
	public ValueTask<CallState> Splice(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var list2Arg = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Space);

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		var list2 = MushText.SplitList(delimiter, list2Arg ?? MarkupText.Empty);

		if (list.Length != list2.Length)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NumberOfWordsMustBeEqual));
		}

		// Each pair uses delimiter as the within-pair separator.
		// Pairs themselves are separated by delimiter + delimiter (double separator)
		// to clearly distinguish pair boundaries in the output.
		var pairs = list.Zip(list2)
			.Select(pair => MarkupText.Concat(pair.First, MarkupText.Concat(delimiter, pair.Second)));
		var betweenPairSep = MarkupText.Concat(delimiter, delimiter);
		var result = MarkupText.Join(betweenPairSep, pairs);

		return ValueTask.FromResult(new CallState(result));
	}

	// (attribute, list, step, delimiter, outsep) — arg 0 is the attribute, arg 2 the group size,
	// matching the helpfile step([<obj>/]<attr>, <list>, <step>[, <delim>[, <osep>]]). The previous
	// names (start, end, increment, expression) were copied from an unrelated numeric-range function
	// and mislabelled every argument in the LSP's inlay hints.
	[SharpFunction(Name = "step", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "step", "delimiter", "outsep"])]
	public async ValueTask<CallState> Step(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var rawAttrArg = parser.CurrentState.Arguments["0"].Message!;
		var rawAttrStr = rawAttrArg.ToPlainText();

		var stepArg = parser.CurrentState.Arguments["2"].Message!.ToPlainText();
		if (!int.TryParse(stepArg, out var step) || step < 1 || step > 30)
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MarkupText.Space);
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 4, delim);
		var list = MushText.SplitList(delim, parser.CurrentState.Arguments["1"].Message!);

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var result = new List<MString>();
			for (var i = 0; i < list.Length; i += step)
			{
				var args = new Dictionary<string, CallState>();
				for (var j = 0; j < step && (i + j) < list.Length; j++)
				{
					args[j.ToString()] = new CallState(list[i + j]);
				}
				result.Add(await AttributeService.EvaluateAttributeFunctionAsync(parser, executor, rawAttrArg, args));
			}
			return new CallState(MarkupText.Join(sep, result));
		}

		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(ErrorMessages.Returns.ObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			dbref,
			LocateFlags.All);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		var maybeAttr = await AttributeService.GetAttributeAsync(
			executor,
			located,
			attrName,
			mode: IAttributeService.AttributeMode.Execute,
			parent: true);

		if (maybeAttr.IsNone)
		{
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		if (maybeAttr.IsError)
		{
			return new CallState(maybeAttr.AsError.Value);
		}

		var attr = maybeAttr.AsAttribute;
		var attrValue = attr.Last().Value;
		var attrResult = new List<MString>();

		for (var i = 0; i < list.Length; i += step)
		{
			var args = new Dictionary<string, CallState>();
			var envRegs = new Dictionary<string, CallState>();

			for (var j = 0; j < step && (i + j) < list.Length; j++)
			{
				args[j.ToString()] = new CallState(list[i + j]);
				envRegs[j.ToString()] = new CallState(list[i + j]);
			}

			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = args,
				EnvironmentRegisters = envRegs
			});
			attrResult.Add((await newParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MarkupText.Join(sep, attrResult));
	}

	/// <summary>
	/// PennMUSH <c>strfirstof()</c>: the first argument that evaluates to a non-empty string, or the
	/// last argument when none does. NoParse only defers evaluation — the value handed back is the
	/// EVALUATED one, never the source text. Returning the raw text instead
	/// (<c>strfirstof(add(1,1),7)</c> answering <c>add(1,1)</c>) is what
	/// <c>fun_strfirstof</c> in <c>src/funmisc.c</c> never does, and it broke every caller that wrote
	/// <c>strfirstof(r(page,args),1)</c> — the unevaluated text carries a comma, which then split the
	/// argument list of whatever consumed it.
	/// </summary>
	[SharpFunction(Name = "strfirstof", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["expression...", "default"])]
	public async ValueTask<CallState> StringFirstOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var argsArray = parser.CurrentState.ArgumentsOrdered.ToArray();

		for (var i = 0; i < argsArray.Length - 1; i++)
		{
			var parsed = await argsArray[i].Value.ParsedMessage();
			if (!string.IsNullOrEmpty(parsed?.ToPlainText()))
			{
				return new CallState(parsed);
			}
		}

		return new CallState(await argsArray[^1].Value.ParsedMessage() ?? MarkupText.Empty);
	}

	[SharpFunction(Name = "strallof", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["expression..."])]
	public ValueTask<CallState> StringAllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		// Last arg is the output delimiter; return all non-empty results joined by it.
		if (args.Count < 2)
		{
			return ValueTask.FromResult(CallState.Empty);
		}

		var delimiter = args[(args.Count - 1).ToString()].Message ?? MarkupText.Empty;
		var nonEmptyValues = Enumerable.Range(0, args.Count - 1)
			.Select(i => args[i.ToString()].Message ?? MarkupText.Empty)
			.Where(value => value.Length > 0);

		return ValueTask.FromResult(new CallState(MarkupText.Join(delimiter, nonEmptyValues)));
	}

	[SharpFunction(Name = "table", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list", "width", "delimiter", "line-delimiter"])]
	public async ValueTask<CallState> Table(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var fieldWidthArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "10").ToPlainText();
		var lineWidthArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "78").ToPlainText();
		var delimiterArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, " ");
		var separatorArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, " ");
		var fieldAlignment = "<";

		if (fieldWidthArg.StartsWith('<') || fieldWidthArg.StartsWith('>') || fieldWidthArg.StartsWith('-'))
		{
			fieldAlignment = fieldWidthArg[..1];
			fieldWidthArg = fieldWidthArg[1..];
		}

		if (!int.TryParse(fieldWidthArg, out var fieldWidth))
		{
			return new CallState(ErrorMessages.Returns.InvalidFieldWidth);
		}

		if (!int.TryParse(lineWidthArg, out var lineWidth))
		{
			return new CallState(ErrorMessages.Returns.InvalidLineWidth);
		}

		var fieldsPerLine = lineWidth / fieldWidth;
		if (fieldsPerLine < 1)
		{
			return new CallState(ErrorMessages.Returns.FieldWidthExceedsLineWidth);
		}
		// Alignment rather than PadType: PadType names the side the *fill* lands on, which is the
		// opposite of the side the text lands on, and this read the two the wrong way round.
		var field = new ColumnFormat
		{
			Width = fieldWidth,
			Alignment = fieldAlignment switch
			{
				">" => Alignment.Right,
				"-" => Alignment.Center,
				_ => Alignment.Left,
			},
		};

		var list = MushText.SplitList(delimiterArg, listArg ?? MarkupText.Empty);
		var resultFields = list.Select(x => x.FormatColumn(field)[0]);

		var lines = resultFields.Chunk(fieldsPerLine);
		var linesWithSeparators = lines.Select(x => MarkupText.Join(separatorArg, x));
		var result = MarkupText.Join(MarkupText.NewLine, linesWithSeparators);

		return new CallState(result);
	}

	[SharpFunction(Name = "unique", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter", "osep"])]
	public ValueTask<CallState> DistinctAndSort(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Plain(""));
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);

		var list = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty);
		var sortTypeStr = sortType.ToPlainText().ToLower();

		// Consecutive duplicates collapse, compared the way the sort type compares.
		static bool SameItem(string sortType, string current, string previous) => sortType switch
		{
			"f" => double.TryParse(current, out var c) && double.TryParse(previous, out var p) && Math.Abs(c - p) < 0.0000001,
			"n" => int.TryParse(current, out var c) && int.TryParse(previous, out var p) && c == p,
			_ => current == previous
		};

		var result = list.Where((item, i) => i == 0 || !SameItem(sortTypeStr, item.Text, list[i - 1].Text));

		return ValueTask.FromResult<CallState>(MarkupText.Join(outputSep, result));
	}

	[SharpFunction(Name = "wordpos", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "number", "delimiter"])]
	public ValueTask<CallState> WordPosition(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list = (args["0"].Message ?? MarkupText.Empty).ToPlainText();
		var numberArg = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ").ToPlainText();

		if (!int.TryParse(numberArg, out var number))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.PositiveInteger));
		}

		if (number < 1 || number > list.Length)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.WordNumberOutOfRange));
		}

		// fun_wordpos (src/funlist.c) walks the words in place and stops at the first one that ends past
		// the character, so a delimiter belongs to the word after it: wordpos(foo bar baz, 5) is 2. A
		// space delimiter merges its runs, every other delimiter counts an empty word between two of
		// its own, which is what SplitList does with the items.
		var target = number - 1;
		var word = 1;
		var text = list.AsSpan();
		foreach (var range in text.Split(delimiter))
		{
			var (offset, length) = range.GetOffsetAndLength(text.Length);
			if (delimiter == " " && length == 0) continue;
			if (target < offset + length) break;
			word++;
		}

		return ValueTask.FromResult(new CallState(word));
	}

	[SharpFunction(Name = "words", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["string", "delimiter"])]
	public async ValueTask<CallState> ListCount(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Argument indexes are 0-based, and words() takes two arguments: the delimiter is 1. Reading
		// it from 2 meant a delimiter was never seen and words(a|b|c,|) always counted space-separated
		// words.
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, " ");
		var list = MushText.SplitList(delim, (await parser.CurrentState.Arguments["0"].ParsedMessage())!);

		return new CallState(list.Length.ToString());
	}

	[SharpFunction(Name = "linsert", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "position", "new-item", "delim"])]
	public async ValueTask<CallState> ListInsert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var positionArg = args["1"].Message!.ToPlainText();
		var newItemArg = args["2"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, " ");

		if (!int.TryParse(positionArg, out var position))
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		var listItems = MushText.SplitList(delimiter, listArg ?? MarkupText.Empty).ToList();
		var count = listItems.Count;

		// PennMUSH find_list_position logic with insert=true
		int insertIndex;
		if (position < 0)
		{
			// Negative: convert to positive 1-indexed from end
			// i = (total + 1) - abs(position)
			var i = (count + 1) - Math.Abs(position);
			if (i < 1 || i > count)
			{
				// Special case: inserting into an empty list
				if (count == 0 && (i == 0 || i == 1))
				{
					insertIndex = 0; // Will insert as first (only) element
				}
				else
				{
					// Out of range: return original list unchanged
					return new CallState(listArg);
				}
			}
			else
			{
				// insert && negative: return i + 1 (1-indexed), then subtract 1 for 0-indexed
				insertIndex = i; // i+1-1 = i
			}
		}
		else if (position > 0)
		{
			if (position > count)
			{
				// Special case: inserting into an empty list
				if (count == 0 && position == 1)
				{
					insertIndex = 0;
				}
				else
				{
					// Out of range: return original list unchanged
					return new CallState(listArg);
				}
			}
			else
			{
				// Convert 1-indexed to 0-indexed: insert BEFORE this position
				insertIndex = position - 1;
			}
		}
		else
		{
			// Position 0: return original list unchanged (no-op per PennMUSH)
			return new CallState(listArg);
		}

		listItems.Insert(insertIndex, newItemArg ?? MarkupText.Empty);
		return new CallState(MarkupText.Join(delimiter, listItems));
	}

	[SharpFunction(Name = "setunion", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public async ValueTask<CallState> SetUnion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		// PennMUSH: empty delimiter arg means use space (default)
		if (string.IsNullOrEmpty(delimiter.ToPlainText())) delimiter = MarkupText.Space;
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Plain("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MushText.SplitList(delimiter, list1 ?? MarkupText.Empty);
		var aList2 = MushText.SplitList(delimiter, list2 ?? MarkupText.Empty);

		var sortTypeType = SortService.StringToSortType(sortType.ToPlainText());
		var comparer = SortService.GetEqualityComparer(sortTypeType);
		var sorted = SortService.Sort(Enumerable.DistinctBy(aList1
			.Concat(aList2), x => x.ToPlainText(), comparer), (x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MarkupText.Join(outputSeparator, await sorted.ToArrayAsync()));
	}

	[SharpFunction(Name = "setdiff", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public async ValueTask<CallState> SetDifference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		// PennMUSH: empty delimiter arg means use space (default)
		if (string.IsNullOrEmpty(delimiter.ToPlainText())) delimiter = MarkupText.Space;
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Plain("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MushText.SplitList(delimiter, list1 ?? MarkupText.Empty);
		var aList2 = MushText.SplitList(delimiter, list2 ?? MarkupText.Empty);

		var sortTypeType = SortService.StringToSortType(sortType.ToPlainText());
		var comparer = SortService.GetEqualityComparer(sortTypeType);
		var set2 = new HashSet<string>(aList2.Select(x => x.ToPlainText()), comparer);

		var difference = aList1.Where(x => !set2.Contains(x.ToPlainText()));

		var sorted = SortService.Sort(Enumerable.DistinctBy(difference, x => x.ToPlainText(), comparer),
			(x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MarkupText.Join(outputSeparator, await sorted.ToArrayAsync()));
	}

	[SharpFunction(Name = "setinter", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public async ValueTask<CallState> SetIntersection(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		// PennMUSH: empty delimiter arg means use space (default)
		if (string.IsNullOrEmpty(delimiter.ToPlainText())) delimiter = MarkupText.Space;
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Plain("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MushText.SplitList(delimiter, list1 ?? MarkupText.Empty);
		var aList2 = MushText.SplitList(delimiter, list2 ?? MarkupText.Empty);

		var sortTypeType = SortService.StringToSortType(sortType.ToPlainText());
		var comparer = SortService.GetEqualityComparer(sortTypeType);
		var set2 = new HashSet<string>(aList2.Select(x => x.ToPlainText()), comparer);

		var intersection = aList1.Where(x => set2.Contains(x.ToPlainText()));

		var sorted = SortService.Sort(Enumerable.DistinctBy(intersection, x => x.ToPlainText(), comparer),
			(x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MarkupText.Join(outputSeparator, await sorted.ToArrayAsync()));
	}

	[SharpFunction(Name = "setsymdiff", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public async ValueTask<CallState> SetSymmetricalDifference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Space);
		// PennMUSH: empty delimiter arg means use space (default)
		if (string.IsNullOrEmpty(delimiter.ToPlainText())) delimiter = MarkupText.Space;
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MarkupText.Plain("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MushText.SplitList(delimiter, list1 ?? MarkupText.Empty);
		var aList2 = MushText.SplitList(delimiter, list2 ?? MarkupText.Empty);

		var sortTypeType = SortService.StringToSortType(sortType.ToPlainText());
		var comparer = SortService.GetEqualityComparer(sortTypeType);
		var set1 = new HashSet<string>(aList1.Select(x => x.ToPlainText()), comparer);
		var set2 = new HashSet<string>(aList2.Select(x => x.ToPlainText()), comparer);

		var symdiff = aList1.Where(x => !set2.Contains(x.ToPlainText()))
			.Concat(aList2.Where(x => !set1.Contains(x.ToPlainText())));

		var sorted = SortService.Sort(Enumerable.DistinctBy(symdiff, x => x.ToPlainText(), comparer),
			(x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MarkupText.Join(outputSeparator, await sorted.ToArrayAsync()));
	}

	/// <summary>
	/// Evaluates a #lambda or #apply expression for each item in a list.
	/// </summary>
	private async ValueTask<List<MString>> EvaluateLambdaOrApplyForEachItemAsync(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		MString rawAttrArg,
		MString[] list)
	{
		var results = new List<MString>(list.Length);
		foreach (var item in list)
		{
			var evaluated = await AttributeService.EvaluateAttributeFunctionAsync(
				parser,
				executor,
				rawAttrArg,
				new Dictionary<string, CallState> { { "0", new CallState(item) } });
			results.Add(evaluated);
		}

		return results;
	}
}