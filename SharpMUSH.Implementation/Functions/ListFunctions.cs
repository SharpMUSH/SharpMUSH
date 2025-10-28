using MoreLinq.Extensions;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "elements", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "extract", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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
			// TODO: Indicate arg number.
			return new CallState(Errors.ErrorInteger);
		}

		if (!int.TryParse(length, out var lengthNumber))
		{
			// TODO: Indicate arg number.
			return new CallState(Errors.ErrorInteger);
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

	[SharpFunction(Name = "filter", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Filter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "filterbool", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> FilterBool(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "first", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "fold", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Fold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "grab", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Grab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "graball", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> GrabAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "index", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Index(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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

	[SharpFunction(Name = "items", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Items(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "itemize", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "inum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> IterationNumber(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "last", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Last(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MModule.split2(delim, listArg);
		var last = list.LastOrDefault() ?? MModule.empty();

		return ValueTask.FromResult(new CallState(last));
	}

	[SharpFunction(Name = "ldelete", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ListDelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "map", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "match", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "matchall", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
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

	[SharpFunction(Name = "member", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Member(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var list = args["0"].Message!;
		var word = args["1"].Message!.ToPlainText();
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ");
		var splitList = MModule.split2(delimiter, list).Select(x => x.ToPlainText()).ToList();

		return ValueTask.FromResult<CallState>(splitList.IndexOf(word) + 1);
	}

	[SharpFunction(Name = "mix", MinArgs = 3, MaxArgs = 35, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Mix(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "munge", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Munge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "namegrab", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NameGrab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "namegraball", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> NameGrabAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "randextract", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RandomExtract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "rest", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Rest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, " ");
		var list = MModule.split2(delim, parser.CurrentState.Arguments["0"].Message);

		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(delim, list.Skip(1))));
	}

	[SharpFunction(Name = "revwords", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ReverseList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, " ");
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MModule.split2(
			delim,
			(await parser.CurrentState.Arguments["0"].ParsedMessage())!) ?? [];

		return new CallState(MModule.multipleWithDelimiter(sep, list.Reverse()));
	}

	[SharpFunction(Name = "shuffle", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
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

	[SharpFunction(Name = "sort", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Sort(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var list = parser.CurrentState.Arguments["0"].Message!;
		var sortType = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 2, MModule.single(" "));
		var outputSeparator = ArgHelpers.NoParseDefaultNoParseArgument(orderedArgs, 3, delimiter);
		var listItems = MModule.split2(delimiter, list);

		return MModule.multipleWithDelimiter(outputSeparator,
			await SortService!.Sort(listItems, x => x.ToPlainText(), parser, SortService.StringToSortType(sortType)));
	}

	[SharpFunction(Name = "sortby", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SortBy(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "sortkey", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SortKey(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "splice", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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
	public static ValueTask<CallState> Step(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// step([<obj>/]<attr>, <list>, <step>[, <delim>[, <osep>]])

		throw new NotImplementedException();
	}

	[SharpFunction(Name = "strfirstof", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> StringFirstOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var firstOne = orderedArgs.FirstOrDefault(x
				=> !string.IsNullOrEmpty(x.Value.ParsedMessage().GetAwaiter().GetResult()!.ToPlainText()),
			orderedArgs.Last());
		return ValueTask.FromResult(new CallState(firstOne.Value.Message));
	}

	[SharpFunction(Name = "strallof", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> StringAllOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
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
		throw new NotImplementedException();
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

	[SharpFunction(Name = "setunion", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
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
			.Concat(aList2), MModule.plainText), x => x.ToPlainText(), parser, sortTypeType);

		return new CallState(MModule.multipleWithDelimiter(outputSeparator, sorted));
	}

	[SharpFunction(Name = "setdiff", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SetDifference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "setinter", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SetIntersection(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "setsymdiff", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SetSymmetricalDifference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}