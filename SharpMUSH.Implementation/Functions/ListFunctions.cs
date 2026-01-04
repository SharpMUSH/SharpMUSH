using MoreLinq.Extensions;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "elements", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "positions", "delimiter"])]
	public static async ValueTask<CallState> Elements(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var numbersArg = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sep = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);

		var list = MModule.split2(delimiter, listArg);
		var numbers = numbersArg.Split(" ");

		var result = list.Where((_, i) => numbers.Contains(i.ToString()));

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "elist", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SeperatedList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var space = MModule.single(" ");
		var list = parser.CurrentState.ArgumentsOrdered["0"].Message!;
		var conjunction = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "and");
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, space);
		var outSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delim);
		var punctuation = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, ",");
		var splitList = MModule.split2(delim, list) ?? [];

		if (splitList.Length > 2)
		{
			splitList[^1] = MModule.concat(
				MModule.concat(
					conjunction,
					MModule.concat(outSeparator, splitList[^1])),
				outSeparator);
		}

		return ValueTask.FromResult<CallState>(
			MModule.multipleWithDelimiter(
				MModule.concat(punctuation, outSeparator),
				splitList));
	}

	[SharpFunction(Name = "extract", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "start", "length", "delimiter"])]
	public static async ValueTask<CallState> Extract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var first = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single("1")).ToPlainText();
		var length = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single("1")).ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single(" "));

		if (!int.TryParse(first, out var firstNumber))
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "FIRST (arg 2)"));
		}

		if (!int.TryParse(length, out var lengthNumber))
		{
			return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "LENGTH (arg 3)"));
		}

		var list = MModule.split2(delimiter, listArg);
		var range = firstNumber > 0
			? list.Skip(firstNumber - 1)
			: Enumerable.TakeLast(list, Math.Abs(firstNumber));
		var result = lengthNumber > 0
			? range.Take(lengthNumber)
			: Enumerable.TakeLast(range, Math.Abs(lengthNumber));

		return new CallState(MModule.multipleWithDelimiter(delimiter, result));
	}

	[SharpFunction(Name = "filter", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter"])]
	public static async ValueTask<CallState> Filter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Arg0: Object/Attribute
		// Arg1: List
		// Arg2: Delimiter (optional)
		// Arg3: Output separator (optional)
		// Arg4+: Additional arguments passed as v(1) through v(30)

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);

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
			var parsed = (await newParser.FunctionParse(attrValue))!.Message!;
			
			if (parsed.ToPlainText() == "1")
			{
				result.Add(item);
			}
		}

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "filterbool", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public static async ValueTask<CallState> FilterBool(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);

		// Build environment registers for additional arguments (v(1) to v(30))
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
			var parsed = (await newParser.FunctionParse(attrValue))!;
			
			// FilterBool returns items where the function evaluates to a boolean true
			if (parsed.Message!.Truthy())
			{
				result.Add(item);
			}
		}

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "first", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public static ValueTask<CallState> FirstInList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MModule.split2(delim, listArg);
		var first = list.FirstOrDefault() ?? MModule.empty();

		return ValueTask.FromResult(new CallState(first));
	}

	[SharpFunction(Name = "firstof", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> FirstOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var first = await parser.CurrentState.ArgumentsOrdered
			.ToAsyncEnumerable()
			.Select(x => x.Value)
			.FirstOrDefaultAsync(async (x, _) => (await x.ParsedMessage()).Truthy(), CallState.Empty);

		return first;
	}

	[SharpFunction(Name = "fold", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "base"])]
	public static async ValueTask<CallState> Fold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Arg0: Object/Attribute
		// Arg1: List
		// Arg2: Base case (optional)
		// Arg3: Delimiter (optional)

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var baseCase = parser.CurrentState.ArgumentsOrdered.TryGetValue("2", out var baseCaseArg)
			? baseCaseArg.Message
			: null;
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MModule.single(" "));

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);
		if (list.Length == 0)
		{
			return CallState.Empty;
		}

		MString accumulator;
		var startIndex = 0;
		var iteration = 0;

		if (baseCase != null)
		{
			// Base case provided: start with base case as %0 and first element as %1
			accumulator = baseCase;
		}
		else
		{
			// No base case: start with first element as accumulator
			if (list.Length < 2)
			{
				return new CallState(list[0]);
			}
			accumulator = list[0];
			startIndex = 1;
		}

		// Fold through the rest of the list
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

	[SharpFunction(Name = "grab", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public static ValueTask<CallState> Grab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = MModule.plainText(parser.CurrentState.Arguments["1"].Message)!;
		var regPattern = globPattern.GlobToRegex();
		var regex = new System.Text.RegularExpressions.Regex(regPattern,
			System.Text.RegularExpressions.RegexOptions.Singleline);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var splitList = MModule.split2(delimiter, list) ?? [];

		return ValueTask.FromResult<CallState>(splitList
			.FirstOrDefault(x => regex.IsMatch(x.ToPlainText())) ?? MModule.empty());
	}

	[SharpFunction(Name = "graball", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "pattern", "delimiter"])]
	public static ValueTask<CallState> GrabAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = MModule.plainText(parser.CurrentState.Arguments["1"].Message)!;
		var regPattern = globPattern.GlobToRegex();
		var regex = new System.Text.RegularExpressions.Regex(regPattern,
			System.Text.RegularExpressions.RegexOptions.Singleline);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 3, delimiter);
		var splitList = MModule.split2(delimiter, list) ?? [];

		return ValueTask.FromResult<CallState>(
			MModule.multipleWithDelimiter(outputSep, splitList.Where(x => regex.IsMatch(x.ToPlainText()))));
	}

	[SharpFunction(Name = "index", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "element", "delimiter"])]
	public static ValueTask<CallState> Index(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var delimiter = args["1"].Message!;
		var firstArg = args["2"].Message!.ToPlainText();
		var lengthArg = args["3"].Message!.ToPlainText();

		if (!int.TryParse(firstArg, out var first))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		if (!int.TryParse(lengthArg, out var length))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		var list = MModule.split2(delimiter, listArg);
		var range = first > 0
			? list.Skip(first - 1)
			: Enumerable.TakeLast(list, Math.Abs(first));
		var result = length > 0
			? range.Take(length)
			: Enumerable.TakeLast(range, Math.Abs(length));

		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(delimiter, result)));
	}

	[SharpFunction(Name = "iter", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Iter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var listArg = (await parser.CurrentState.Arguments["0"].ParsedMessage())!;

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MModule.split2(delim, listArg);
		var wrappedIteration = new IterationWrapper<MString>
			{ Value = MModule.empty(), Break = false, NoBreak = false, Iteration = 0 };
		var result = new List<MString>();

		parser.CurrentState.IterationRegisters.Push(wrappedIteration);

		foreach (var item in list)
		{
			wrappedIteration.Value = item!;
			wrappedIteration.Iteration++;
			var parsed = await parser.CurrentState.Arguments["1"].ParsedMessage();
			result.Add(parsed!);

			if (wrappedIteration.Break)
			{
				break;
			}
		}

		parser.CurrentState.IterationRegisters.TryPop(out _);

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "items", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public static ValueTask<CallState> Items(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message!;
		var delimiter = parser.CurrentState.Arguments["1"].Message!;

		// items() counts the number of delimiter occurrences + 1
		// This naturally handles null items
		var listStr = list.ToPlainText();
		var delimStr = delimiter.ToPlainText();

		if (string.IsNullOrEmpty(delimStr))
		{
			// If delimiter is empty, each character is an item
			return ValueTask.FromResult(new CallState(listStr.Length));
		}

		var count = 1; // Start with 1 (for the first item)
		var index = 0;
		while ((index = listStr.IndexOf(delimStr, index, StringComparison.Ordinal)) != -1)
		{
			count++;
			index += delimStr.Length;
		}

		return ValueTask.FromResult(new CallState(count));
	}

	[SharpFunction(Name = "itemize", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter", "conjunction", "punctuation"])]
	public static ValueTask<CallState> Itemize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var space = MModule.single(" ");
		var list = parser.CurrentState.ArgumentsOrdered["0"].Message!;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, space);
		var conjunction = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "and");
		var punctuation = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, ",");
		var splitList = MModule.split2(delim, list) ?? [];

		if (splitList.Length > 2)
		{
			splitList[^1] = MModule.concat(
				MModule.concat(
					conjunction,
					MModule.concat(space, splitList[^1])),
				space);
		}

		return ValueTask.FromResult<CallState>(
			MModule.multipleWithDelimiter(
				MModule.concat(punctuation, space),
				splitList));
	}

	[SharpFunction(Name = "ibreak", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly)]
	public static ValueTask<CallState> IterationBreak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var iterDepth = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.single("0"));
		var iterNumber = int.Parse(iterDepth.ToString());
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (iterNumber >= maxCount)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorRegisterRange));
		}

		parser.CurrentState.IterationRegisters.ElementAt(maxCount - iterNumber - 1).Break = true;

		return ValueTask.FromResult(CallState.Empty);
	}

	[SharpFunction(Name = "ilev", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IterationLevel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var depth = parser.CurrentState.IterationRegisters.Count;
		return ValueTask.FromResult(new CallState(depth > 0 ? depth - 1 : -1));
	}

	[SharpFunction(Name = "inum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IterationNumber(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var levelArg = args["0"].Message!.ToPlainText();
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (levelArg.Equals("L", StringComparison.OrdinalIgnoreCase))
		{
			// "L" refers to the outermost iteration
			if (maxCount == 0)
			{
				return ValueTask.FromResult(new CallState(Errors.ErrorRegisterRange));
			}
			return ValueTask.FromResult(new CallState(parser.CurrentState.IterationRegisters.Last().Iteration));
		}

		if (!int.TryParse(levelArg, out var level))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		if (level < 0 || level >= maxCount)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorRegisterRange));
		}

		// Level 0 = current, 1 = parent, etc.
		var iteration = parser.CurrentState.IterationRegisters.ElementAt(maxCount - level - 1).Iteration;
		return ValueTask.FromResult(new CallState(iteration));
	}

	[SharpFunction(Name = "last", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "count", "delimiter"])]
	public static ValueTask<CallState> Last(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MModule.split2(delim, listArg);
		var last = list.LastOrDefault() ?? MModule.empty();

		return ValueTask.FromResult(new CallState(last));
	}

	[SharpFunction(Name = "ldelete", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "position", "delimiter"])]
	public static ValueTask<CallState> ListDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var positionsArg = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);

		var list = MModule.split2(delimiter, listArg);
		var positions = positionsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(p => int.TryParse(p, out var pos) ? pos : (int?)null)
			.Where(p => p.HasValue)
			.Select(p => p!.Value);
		var positionsSet = new HashSet<int>(positions);

		var result = new List<MString>();
		for (var i = 0; i < list.Length; i++)
		{
			var index = i + 1; // 1-based indexing
			var negativeIndex = i - list.Length; // negative indexing from end
			
			// Check if this position should be deleted
			if (!positionsSet.Contains(index) && !positionsSet.Contains(negativeIndex))
			{
				result.Add(list[i]);
			}
		}

		return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(outputSep, result));
	}

	[SharpFunction(Name = "map", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "outsep"])]
	public static async ValueTask<CallState> Map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Arg0: Object/Attribute
		// Arg1: List
		// Arg2: Delim
		// Arg3: Sep

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);

		var result = new List<MString>();
		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
				EnvironmentRegisters = new Dictionary<string, CallState> { { "0", new CallState(item) } }
			});
			result.Add((await newParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "match", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "pattern", "delimiter"])]
	public static ValueTask<CallState> Match(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = MModule.plainText(parser.CurrentState.Arguments["1"].Message)!;
		var regPattern = globPattern.GlobToRegex();
		var regex = new System.Text.RegularExpressions.Regex(regPattern,
			System.Text.RegularExpressions.RegexOptions.Singleline);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var splitList = MModule.split2(delimiter, list) ?? [];

		return ValueTask.FromResult<CallState>(splitList
			.FirstOrDefault(x => regex.IsMatch(x.ToPlainText())) ?? MModule.empty());
	}

	[SharpFunction(Name = "matchall", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "pattern", "delimiter"])]
	public static ValueTask<CallState> MatchAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message;
		var globPattern = MModule.plainText(parser.CurrentState.Arguments["1"].Message)!;
		var regPattern = globPattern.GlobToRegex();
		var regex = new System.Text.RegularExpressions.Regex(regPattern,
			System.Text.RegularExpressions.RegexOptions.Singleline);
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var splitList = MModule.split2(delimiter, list) ?? [];

		return ValueTask.FromResult<CallState>(
			MModule.multipleWithDelimiter(delimiter, splitList.Where(x => regex.IsMatch(x.ToPlainText()))));
	}

	[SharpFunction(Name = "member", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.StripAnsi, ParameterNames = ["list", "element", "delimiter"])]
	public static ValueTask<CallState> Member(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var list = args["0"].Message!;
		var word = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var splitList = MModule.split2(delimiter, list).Select(x => x.ToPlainText()).ToList();

		return ValueTask.FromResult<CallState>(splitList.IndexOf(word) + 1);
	}

	[SharpFunction(Name = "mix", MinArgs = 3, MaxArgs = 35, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list1", "list2", "delimiter", "outsep"])]
	public static async ValueTask<CallState> Mix(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Arg0: Object/Attribute
		// Arg1-Arg30: Up to 30 lists
		// Last arg (if > 2 lists): delimiter

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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

		// Determine delimiter and lists
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
			delimiter = MModule.single(" ");
		}

		// Split all lists
		var lists = new List<MString[]>();
		var maxLength = 0;
		for (var i = 1; i <= listCount; i++)
		{
			var list = MModule.split2(delimiter, parser.CurrentState.ArgumentsOrdered[i.ToString()].Message!);
			lists.Add(list);
			maxLength = Math.Max(maxLength, list.Length);
		}

		// Process each position
		var result = new List<MString>();
		for (var i = 0; i < maxLength; i++)
		{
			var args = new Dictionary<string, CallState>();
			var envRegs = new Dictionary<string, CallState>();

			for (var j = 0; j < lists.Count; j++)
			{
				var value = i < lists[j].Length ? lists[j][i] : MModule.empty();
				args[j.ToString()] = new CallState(value);
				envRegs[j.ToString()] = new CallState(value);
			}

			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = args,
				EnvironmentRegisters = envRegs
			});
			result.Add((await newParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MModule.multipleWithDelimiter(delimiter, result));
	}

	[SharpFunction(Name = "munge", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list1", "list2", "list3", "delimiter"])]
	public static async ValueTask<CallState> Munge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Arg0: Object/Attribute
		// Arg1: List1 (to be transformed)
		// Arg2: List2 (to be rearranged based on list1's transformation)
		// Arg3: Delimiter (optional)
		// Arg4: Output separator (optional)

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 4, delim);

		var list1 = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);
		var list2 = MModule.split2(delim, parser.CurrentState.Arguments["2"].Message!);

		// Pass entire list1 to the function
		var newParser = parser.Push(parser.CurrentState with
		{
			Arguments = new Dictionary<string, CallState>
			{
				{ "0", new CallState(MModule.multipleWithDelimiter(delim, list1)) },
				{ "1", new CallState(delim) }
			},
			EnvironmentRegisters = new Dictionary<string, CallState>
			{
				["0"] = new CallState(MModule.multipleWithDelimiter(delim, list1)),
				["1"] = new CallState(delim)
			}
		});
		var transformedList1Str = (await newParser.FunctionParse(attrValue))!.Message!;
		var transformedList1 = MModule.split2(delim, transformedList1Str);

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

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "namegrab", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NameGrab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var dbrefList = args["0"].Message!.ToPlainText();
		var name = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ").ToPlainText();

		var dbrefs = dbrefList.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
		var dbRefsActualized = dbrefs.Select(HelperFunctions.ParseDbRef).ToArray();

		if (dbRefsActualized.Any(x => x.IsNone()))
		{
			return "INVALID DBREF IN LIST";
		}
		
		var locatedNames = dbRefsActualized.ToAsyncEnumerable().Select(async dbref =>
		{
			var item = await Mediator!.Send(new GetObjectNodeQuery(dbref.AsT0));
			return (dbref.AsT0, item.Object()!.Name);
		});

		var exact = await locatedNames.FirstOrDefaultAsync(async (x,ct) 
			=> (await x).Name == name);

		if (exact != null)
		{
			return (await exact).AsT0;
		}
		
		var partial = await locatedNames.FirstOrDefaultAsync(async (x,ct) 
			=> (await x).Name.Contains(name, StringComparison.OrdinalIgnoreCase));

		if (partial != null)
		{
			return (await partial).AsT0;
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "namegraball", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> NameGrabAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var dbrefList = args["0"].Message!.ToPlainText();
		var name = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ").ToPlainText();

		var dbrefs = dbrefList.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
		var dbRefsActualized = dbrefs.Select(HelperFunctions.ParseDbRef).ToArray();

		if (dbRefsActualized.Any(x => x.IsNone()))
		{
			return "INVALID DBREF IN LIST";
		}
		
		var locatedNames = dbRefsActualized.ToAsyncEnumerable().Select(async dbref =>
		{
			var item = await Mediator!.Send(new GetObjectNodeQuery(dbref.AsT0));
			return (dbref.AsT0, item.Object()!.Name);
		});

		var exact = locatedNames.Where(async (x,ct) 
			=> (await x).Name == name);

		if (await exact.AnyAsync())
		{
			return string.Join(" ", exact.Select(async x => (await x).AsT0.ToString()));
		}
		
		var partial = locatedNames.Where(async (x,ct) 
			=> (await x).Name.Contains(name, StringComparison.OrdinalIgnoreCase));

		if (await partial.AnyAsync())
		{
			return string.Join(" ", partial.Select(async x => (await x).AsT0.ToString()));
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "randextract", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RandomExtract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var countArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single("1")).ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var typeArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single("R")).ToPlainText().ToUpper();
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		if (!int.TryParse(countArg, out var count))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorInteger));
		}

		var list = MModule.split2(delimiter, listArg);
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

		return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(outputSep, result));
	}

	[SharpFunction(Name = "randword", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RandomWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var list = orderedArgs["0"].Message!;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 1, " ");
		return ValueTask.FromResult<CallState>(
			MModule.split2(delimiter, list).RandomSubset(1).FirstOrDefault() ?? MModule.empty());
	}

	[SharpFunction(Name = "remove", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Remove(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var list = parser.CurrentState.Arguments["0"].Message!;
		var words = parser.CurrentState.Arguments["1"].Message!;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 2, " ");

		var splitList = MModule.split2(delimiter, list).ToList();
		var splitWords = MModule.split2(delimiter, words);

		foreach (var word in splitWords)
		{
			var index = splitList.FindIndex(x => x.ToPlainText() == word.ToPlainText());
			if (index != -1)
			{
				splitList.RemoveAt(index);
			}
		}

		return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(delimiter, splitList));
	}

	[SharpFunction(Name = "lreplace", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ListReplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var positionsArg = args["1"].Message!.ToPlainText();
		var newItem = args["2"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single(" "));
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var list = MModule.split2(delimiter, listArg).ToList();
		var positions = positionsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(p => int.TryParse(p, out var pos) ? pos : (int?)null)
			.Where(p => p.HasValue)
			.Select(p => p!.Value);
		var positionsSet = new HashSet<int>(positions);

		for (var i = 0; i < list.Count; i++)
		{
			var index = i + 1; // 1-based indexing
			var negativeIndex = i - list.Count; // negative indexing from end
			
			// Check if this position should be replaced
			if (positionsSet.Contains(index) || positionsSet.Contains(negativeIndex))
			{
				list[i] = newItem;
			}
		}

		return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(outputSep, list));
	}

	[SharpFunction(Name = "rest", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public static ValueTask<CallState> Rest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, " ");
		var list = MModule.split2(delim, parser.CurrentState.Arguments["0"].Message);

		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(delim, list.Skip(1))));
	}

	[SharpFunction(Name = "revwords", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public static async ValueTask<CallState> ReverseList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, " ");
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MModule.split2(
			delim,
			(await parser.CurrentState.Arguments["0"].ParsedMessage())!) ?? [];

		return new CallState(MModule.multipleWithDelimiter(sep, list.Reverse()));
	}

	[SharpFunction(Name = "shuffle", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public static ValueTask<CallState> Shuffle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var sep = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, delimiter);

		var list = MModule.split2(delimiter, listArg) ?? [];
		var shuffled = ShuffleExtension.Shuffle(list);
		var result = MModule.multipleWithDelimiter(sep, shuffled);

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "sort", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list", "sort-type", "delimiter", "outsep"])]
	public static async ValueTask<CallState> Sort(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message!;
		var sortType = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 2, MModule.single(" "));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 3, delimiter);
		var listItems = MModule.split2(delimiter, list);

		var sorted = await SortService!.Sort(listItems, (x, ct) => ValueTask.FromResult(x.ToPlainText()), parser,
			SortService!.StringToSortType(sortType));

		return MModule.multipleWithDelimiter(outputSeparator, await sorted.ToArrayAsync());
	}

	[SharpFunction(Name = "sortby", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "list", "delimiter", "outsep"])]
	public static async ValueTask<CallState> SortBy(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!).ToList();

		// Custom comparison using the user-defined function
		// We need to do this synchronously since List.Sort doesn't support async
		var comparisonTasks = new List<Task<(int index, MString value, int order)>>();
		for (var i = 0; i < list.Count; i++)
		{
			var index = i;
			comparisonTasks.Add(Task.Run(async () =>
			{
				var orderSum = 0;
				for (var j = 0; j < list.Count; j++)
				{
					if (i == j) continue;
					
					var newParser = parser.Push(parser.CurrentState with
					{
						Arguments = new Dictionary<string, CallState>
						{
							{ "0", new CallState(list[index]) },
							{ "1", new CallState(list[j]) }
						},
						EnvironmentRegisters = new Dictionary<string, CallState>
						{
							["0"] = new CallState(list[index]),
							["1"] = new CallState(list[j])
						}
					});
					var result = (await newParser.FunctionParse(attrValue))!.Message!.ToPlainText();
					if (int.TryParse(result, out var cmp))
					{
						orderSum += cmp > 0 ? 1 : (cmp < 0 ? -1 : 0);
					}
				}
				return (index, list[index], orderSum);
			}));
		}

		var results = await Task.WhenAll(comparisonTasks);
		var sorted = results.OrderBy(r => r.order).Select(r => r.value);

		return new CallState(MModule.multipleWithDelimiter(sep, sorted));
	}

	[SharpFunction(Name = "sortkey", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> SortKey(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// sortkey([<obj>/]<attrib>, <list>[, <sort type>[, <delimiter>[, <osep>]]])

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var sortType = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(""));
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 4, delim);

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);

		// Generate keys for each element
		var keys = new List<string>();
		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
				EnvironmentRegisters = new Dictionary<string, CallState> { ["0"] = new CallState(item) }
			});
			var key = (await newParser.FunctionParse(attrValue))!.Message!.ToPlainText();
			keys.Add(key);
		}

		// Sort keys with their indices and use standard LINQ OrderBy with a comparison
		var sortTypeStr = sortType.ToPlainText().ToLower();
		
		// Create pairs of (index, key)
		var indexedKeys = keys.Select((k, i) => new { Index = i, Key = k }).ToList();
		
		// Use simple LINQ OrderBy based on keys
		IEnumerable<int> sortedIndices = sortTypeStr switch
		{
			"n" => indexedKeys.OrderBy(x => int.TryParse(x.Key, out var n) ? n : 0).Select(x => x.Index),
			"f" => indexedKeys.OrderBy(x => double.TryParse(x.Key, out var f) ? f : 0.0).Select(x => x.Index),
			"i" => indexedKeys.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Index),
			_ => indexedKeys.OrderBy(x => x.Key).Select(x => x.Index)
		};

		var result = sortedIndices.Select(i => list[i]);
		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "splice", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "position", "delimiter"])]
	public static async ValueTask<CallState> Splice(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var list2Arg = args["1"].Message;
		var wordArg = args["2"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single(" "));

		var list = MModule.split2(delimiter, listArg);
		var list2 = MModule.split2(delimiter, list2Arg);

		if (list.Length != list2.Length)
		{
			return new CallState("#-1 NUMBER OF WORDS MUST BE EQUAL");
		}

		var zippedList = list.Zip(list2);
		var spliced = zippedList.Select(pair => pair.First.ToPlainText() == wordArg ? pair.Second : pair.First);
		var result = MModule.multipleWithDelimiter(delimiter, spliced);

		return new CallState(result);
	}

	[SharpFunction(Name = "step", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Step(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// step([<obj>/]<attr>, <list>, <step>[, <delim>[, <osep>]])

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalObjectAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

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
		var stepArg = parser.CurrentState.Arguments["2"].Message!.ToPlainText();
		
		if (!int.TryParse(stepArg, out var step) || step < 1 || step > 30)
		{
			return new CallState(Errors.ErrorInteger);
		}

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 4, delim);

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);
		var result = new List<MString>();

		// Process in chunks of 'step' size
		for (var i = 0; i < list.Length; i += step)
		{
			var args = new Dictionary<string, CallState>();
			var envRegs = new Dictionary<string, CallState>();

			// Add up to 'step' elements as %0-%9 and v(10)-v(29)
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
			result.Add((await newParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "strfirstof", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> StringFirstOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		
		// If single argument, split by spaces and return first non-empty element
		if (orderedArgs.Count == 1)
		{
			var singleArg = await parser.FunctionParse(orderedArgs["0"].Message!);
			var elements = singleArg!.Message!.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			return elements.Length > 0 
				? new CallState(MModule.single(elements[0]))
				: CallState.Empty;
		}
		
		// Original multi-argument logic: iterate through arguments to find the first non-empty one after parsing
		var argsArray = orderedArgs.ToArray();
		for (int i = 0; i < argsArray.Length - 1; i++)
		{
			var parsedMessage = await argsArray[i].Value.ParsedMessage();
			if (!string.IsNullOrEmpty(parsedMessage?.ToPlainText()))
			{
				return new CallState(argsArray[i].Value.Message);
			}
		}
		
		// If no non-empty argument found, return the last argument
		return new CallState(argsArray[^1].Value.Message);
	}

	[SharpFunction(Name = "strallof", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StringAllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		
		// If single argument, just return it as-is
		if (orderedArgs.Count == 1)
		{
			return ValueTask.FromResult(new CallState(orderedArgs["0"].Message));
		}
		
		// Original multi-argument logic: join all but last with last as delimiter
		var allOf = Enumerable.SkipLast(orderedArgs, 1)
			.Select(x => x.Value.Message!)
			.Where(x => !string.IsNullOrEmpty(x.ToPlainText()));
		var result = MModule.multipleWithDelimiter(
			orderedArgs.Last().Value.Message!,
			allOf);
		return ValueTask.FromResult(new CallState(result));
	}

	[SharpFunction(Name = "table", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Table(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
			return new CallState("#-1 INVALID FIELD WIDTH");
		}

		if (!int.TryParse(lineWidthArg, out var lineWidth))
		{
			return new CallState("#-1 INVALID LINE WIDTH");
		}

		var fieldsPerLine = lineWidth / fieldWidth;
		var list = MModule.split2(delimiterArg, listArg);
		var resultFields = list.Select(x =>
			MModule.pad(x,
				MModule.single(" "),
				fieldWidth,
				fieldAlignment switch
				{
					">" => MModule.PadType.Right,
					"-" => MModule.PadType.Center,
					_ => MModule.PadType.Left
				}, MModule.TruncationType.Truncate));

		var lines = resultFields.Chunk(fieldsPerLine);
		var linesWithSeparators = lines.Select(x => MModule.multipleWithDelimiter(separatorArg, x));
		var result = MModule.multipleWithDelimiter(MModule.single("\n"), linesWithSeparators);

		return new CallState(result);
	}

	[SharpFunction(Name = "unique", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> DistinctAndSort(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(""));
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var outputSep = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);

		var list = MModule.split2(delimiter, listArg);
		
		// Remove consecutive duplicates based on sort type comparison
		var result = new List<MString>();
		var sortTypeStr = sortType.ToPlainText();
		
		for (var i = 0; i < list.Length; i++)
		{
			if (i == 0)
			{
				result.Add(list[i]);
			}
			else
			{
				var current = list[i].ToPlainText();
				var previous = list[i - 1].ToPlainText();
				
				// Compare based on sort type
				var isDuplicate = sortTypeStr.ToLower() switch
				{
					"f" => double.TryParse(current, out var c1) && double.TryParse(previous, out var p1) && Math.Abs(c1 - p1) < 0.0000001,
					"n" => int.TryParse(current, out var c2) && int.TryParse(previous, out var p2) && c2 == p2,
					_ => current == previous
				};
				
				if (!isDuplicate)
				{
					result.Add(list[i]);
				}
			}
		}

		return ValueTask.FromResult<CallState>(MModule.multipleWithDelimiter(outputSep, result));
	}

	[SharpFunction(Name = "wordpos", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> WordPosition(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var numberArg = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ");

		if (!int.TryParse(numberArg, out var number))
		{
			return new CallState(Errors.ErrorUInteger);
		}

		var list = MModule.split2(delimiter, listArg);
		var lengths = list.Select(x => x.Length).ToList();

		if (number > lengths.Sum())
		{
			return new CallState("#-1 WORD NUMBER OUT OF RANGE");
		}

		var i = 0;
		var result = lengths.TakeWhile(x => (i += x) <= number).Count();
		return new CallState(result);
	}

	[SharpFunction(Name = "words", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListCount(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, " ");
		var list = MModule.split2(delim, (await parser.CurrentState.Arguments["0"].ParsedMessage())!);

		return new CallState(list.Length.ToString());
	}

	[SharpFunction(Name = "linsert", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ListInsert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var positionArg = args["1"].Message!.ToPlainText();
		var newItemArg = args["2"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, " ");

		if (!int.TryParse(positionArg, out var position))
		{
			return new CallState(Errors.ErrorUInteger);
		}

		var list = MModule.split2(delimiter, listArg);
		var result = InsertExtension.Insert(list, [newItemArg], position);
		return new CallState(MModule.multipleWithDelimiter(delimiter, result));
	}

	[SharpFunction(Name = "setunion", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public static async ValueTask<CallState> SetUnion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MModule.split2(delimiter, list1);
		var aList2 = MModule.split2(delimiter, list2);

		var sortTypeType = SortService!.StringToSortType(sortType.ToPlainText());
		var sorted = await SortService.Sort(Enumerable.DistinctBy(aList1
			.Concat(aList2), MModule.plainText), (x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MModule.multipleWithDelimiter(outputSeparator, await sorted.ToArrayAsync()));
	}

	[SharpFunction(Name = "setdiff", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public static async ValueTask<CallState> SetDifference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MModule.split2(delimiter, list1);
		var aList2 = MModule.split2(delimiter, list2);
		var set2 = new HashSet<string>(aList2.Select(MModule.plainText));

		// Elements in list1 that aren't in list2
		var difference = aList1.Where(x => !set2.Contains(MModule.plainText(x)));

		var sortTypeType = SortService!.StringToSortType(sortType.ToPlainText());
		var sorted = await SortService.Sort(Enumerable.DistinctBy(difference, MModule.plainText),
			(x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MModule.multipleWithDelimiter(outputSeparator, await sorted.ToArrayAsync()));
	}

	[SharpFunction(Name = "setinter", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter"])]
	public static async ValueTask<CallState> SetIntersection(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MModule.split2(delimiter, list1);
		var aList2 = MModule.split2(delimiter, list2);
		var set2 = new HashSet<string>(aList2.Select(MModule.plainText));

		// Elements that appear in both lists
		var intersection = aList1.Where(x => set2.Contains(MModule.plainText(x)));

		var sortTypeType = SortService!.StringToSortType(sortType.ToPlainText());
		var sorted = await SortService.Sort(Enumerable.DistinctBy(intersection, MModule.plainText),
			(x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MModule.multipleWithDelimiter(outputSeparator, await sorted.ToArrayAsync()));
	}

	[SharpFunction(Name = "setsymdiff", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> SetSymmetricalDifference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sortType = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single("m"));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MModule.split2(delimiter, list1);
		var aList2 = MModule.split2(delimiter, list2);
		var set1 = new HashSet<string>(aList1.Select(MModule.plainText));
		var set2 = new HashSet<string>(aList2.Select(MModule.plainText));

		// Elements that appear in only one of the lists
		var symdiff = aList1.Where(x => !set2.Contains(MModule.plainText(x)))
			.Concat(aList2.Where(x => !set1.Contains(MModule.plainText(x))));

		var sortTypeType = SortService!.StringToSortType(sortType.ToPlainText());
		var sorted = await SortService.Sort(Enumerable.DistinctBy(symdiff, MModule.plainText),
			(x, ct) => ValueTask.FromResult(x.ToPlainText()), parser, sortTypeType);

		return new CallState(MModule.multipleWithDelimiter(outputSeparator, await sorted.ToArrayAsync()));
	}
}