using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
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
		var delimiter = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sep = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, delimiter);

		var list = MModule.split2(delimiter, listArg);
		var numbers = numbersArg.Split(" ");

		var result = list.Where((item, i) => numbers!.Contains(i.ToString()));

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "ELIST", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> elist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "extract", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Extract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var first = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single("1")).ToPlainText();
		var length = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single("1")).ToPlainText();
		var delimiter = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single(" "));

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

	[SharpFunction(Name = "FILTER", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> filter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FILTERBOOL", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> filterbool(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "first", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> FirstInList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MModule.split2(delim, listArg);
		var first = list.FirstOrDefault() ?? MModule.empty();

		return ValueTask.FromResult(new CallState(first));
	}

	[SharpFunction(Name = "firstof", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> FirstOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var first = parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value)
			.FirstOrDefault(x => x.ParsedMessage().ConfigureAwait(false).GetAwaiter().GetResult().Truthy(), CallState.Empty);

		return first;
	}

	[SharpFunction(Name = "FOLD", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> fold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "GRAB", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> grab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "GRABALL", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> graball(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "INDEX", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> index(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "iter", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.NoParse)]
	public static async ValueTask<CallState> Iter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var listArg = (await parser.CurrentState.Arguments["0"].ParsedMessage())!;

		var delim = await Common.ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await Common.ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
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

	[SharpFunction(Name = "ITEMS", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> items(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ITEMIZE", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> itemize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ibreak", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly)]
	public static ValueTask<CallState> IBreak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var iterDepth = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.single("0"));
		var iterNumber = int.Parse(iterDepth.ToString());
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (iterNumber >= maxCount)
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorRange));
		}

		parser.CurrentState.IterationRegisters.ElementAt(maxCount - iterNumber - 1).Break = true;

		return ValueTask.FromResult(CallState.Empty);
	}

	[SharpFunction(Name = "ILEV", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ILev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "INUM", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> INum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "last", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Last(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MModule.split2(delim, listArg);
		var last = list.LastOrDefault() ?? MModule.empty();

		return ValueTask.FromResult(new CallState(last));
	}

	[SharpFunction(Name = "LDELETE", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ldelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		var delim = await Common.ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await Common.ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);

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
		var str = parser.CurrentState.Arguments["0"].Message;
		var globPattern = MModule.plainText(parser.CurrentState.Arguments["1"].Message)!;
		var regPattern = globPattern.GlobToRegex();

		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MATCHALL", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> matchall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MEMBER", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> member(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MIX", MinArgs = 3, MaxArgs = 35, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> mix(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MUNGE", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> munge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NAMEGRAB", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> namegrab(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NAMEGRABALL", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> namegraball(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "RANDEXTRACT", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> randextract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "RANDWORD", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> randword(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REMOVE", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> remove(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LREPLACE", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> lreplace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "rest", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Rest(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delim = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 1, " ");
		var list = MModule.split2(delim, parser.CurrentState.Arguments["0"].Message);

		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(delim, list.Skip(1))));
	}

	[SharpFunction(Name = "RESTARTS", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> restarts(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "RESTARTTIME", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> restarttime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "revwords", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> ReverseList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var delim = await Common.ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, " ");
		var sep = await Common.ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MModule.split2(
			delim,
			(await parser.CurrentState.Arguments["0"].ParsedMessage())!);

		return new CallState(MModule.multipleWithDelimiter(sep, list.Reverse()));
	}

	[SharpFunction(Name = "shuffle", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Shuffle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;

		var args = parser.CurrentState.ArgumentsOrdered;
		var listArg = args["0"].Message;
		var delimiter = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.single(" "));
		var sep = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, delimiter);

		var list = MModule.split2(delimiter, listArg);
		var shuffled = MoreLinq.Extensions.ShuffleExtension.Shuffle(list);
		var result = MModule.multipleWithDelimiter(sep, shuffled);

		return new CallState(result);
	}

	[SharpFunction(Name = "SORT", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> sort(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SORTBY", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> sortby(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SORTKEY", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> sortkey(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		var delimiter = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single(" "));

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

	[SharpFunction(Name = "STEP", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> step(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
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
		var fieldWidthArg = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 1, "10").ToPlainText();
		var lineWidthArg = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, "78").ToPlainText();
		var delimiterArg = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, " ");
		var separatorArg = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 4, " ");
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

	[SharpFunction(Name = "UNIQUE", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
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
		var delimiter = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ");

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
		var delim = await Common.ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, " ");
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
		var delimiter = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, " ");

		if (!int.TryParse(positionArg, out var position))
		{
			return new CallState(Errors.ErrorUInteger);
		}

		var list = MModule.split2(delimiter, listArg);
		var result = MoreLinq.Extensions.InsertExtension.Insert(list, [newItemArg], position);
		return new CallState(MModule.multipleWithDelimiter(delimiter, result));
	}

	[SharpFunction(Name = "SETUNION", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> setunion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list1 = args["0"].Message;
		var list2 = args["1"].Message;
		var delimiter = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MModule.single(" "));
		var sortType = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 3, MModule.single("m"));
		var outputSeparator = Common.ArgHelpers.NoParseDefaultNoParseArgument(args, 4, delimiter);

		var aList1 = MModule.split2(delimiter, list1);
		var aList2 = MModule.split2(delimiter, list2);

		var result = await aList1
			.Concat(aList2)
			.DistinctBy(MModule.plainText)
			.OrderByAsync(x => x.ToPlainText(), parser, Mediator!, LocateService!, ConnectionService!,
				sortType.ToPlainText());

		return new CallState(MModule.multipleWithDelimiter(outputSeparator, result));
	}

	[SharpFunction(Name = "SETDIFF", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setmanip(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SETINTER", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setinter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "SETSYMDIFF", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> setsmydiff(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}