using System.Text.RegularExpressions;
using MoreLinq.Extensions;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "ELEMENTS", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> elements(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ELIST", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> elist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "EXTRACT", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> extract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
		var delim = NoParseDefaultNoParseArgument(parser.CurrentState.Arguments, 1, MModule.single(" "));
		var listArg = parser.CurrentState.Arguments["0"].Message;
		var list = MModule.split2(delim, listArg);
		var first = list.FirstOrDefault() ?? MModule.empty();

		return ValueTask.FromResult(new CallState(first));
	}

	[SharpFunction(Name = "FIRSTOF", MinArgs = 0, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> firstof(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FOLD", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> fold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "FOLDERSTATS", MinArgs = 0, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> folderstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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

		// TODO: Evaluate delim and sep.
		var delim = await NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MModule.split2(delim, listArg);
		var wrappedIteration = new IterationWrapper<MString> { Value = MModule.empty() };
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
		var iterDepth = NoParseDefaultNoParseArgument(parser.CurrentState.Arguments, 0, MModule.single("0"));
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
		var delim = NoParseDefaultNoParseArgument(parser.CurrentState.Arguments, 1, MModule.single(" "));
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

		var executor = (await parser.CurrentState.ExecutorObject(parser.Database)).Known();
		var enactor = (await parser.CurrentState.EnactorObject(parser.Database)).Known();
		var objAttr =
			HelperFunctions.SplitOptionalDBRefAndAttr(MModule.plainText(parser.CurrentState.Arguments["0"].Message!));
		if (objAttr is { IsT1: true, AsT1: false })
		{
			return new CallState(Errors.ErrorObjectAttributeString);
		}

		var (dbref, attrName) = objAttr.AsT0;
		dbref ??= executor.ToString();

		var locate = await parser.LocateService.LocateAndNotifyIfInvalid(
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

		var maybeAttr = await parser.AttributeService.GetAttributeAsync(
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
		var attrValue = attr.Value;
		var delim = await NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));
		var sep = await NoParseDefaultEvaluatedArgument(parser, 3, delim);

		var list = MModule.split2(delim, parser.CurrentState.Arguments["1"].Message!);

		var result = new List<MString>();
		foreach (var item in list)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				Arguments = new(new Dictionary<string, CallState> { { "0", new CallState(item) } })
			});
			result.Add((await newParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MModule.multipleWithDelimiter(sep, result));
	}

	[SharpFunction(Name = "MATCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> match(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		var delim = NoParseDefaultNoParseArgument(parser.CurrentState.Arguments, 1, " ");
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
		var delim = await NoParseDefaultEvaluatedArgument(parser, 2, " ");
		var sep = await NoParseDefaultEvaluatedArgument(parser, 3, delim);
		var list = MModule.split2(
			delim,
			(await parser.CurrentState.Arguments["0"].ParsedMessage())!);

		return new CallState(MModule.multipleWithDelimiter(sep, list.Reverse()));
	}

	[SharpFunction(Name = "SHUFFLE", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> shuffle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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

	[SharpFunction(Name = "SPLICE", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> splice(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "STEP", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> step(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "STRFIRSTOF", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public static ValueTask<CallState> strfirstof(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "TABLE", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> table(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "UNIQUE", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> DistinctAndSort(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WORDPOS", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> wordpos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WORDS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ListCount(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var delim = await NoParseDefaultEvaluatedArgument(parser, 2, " ");
		var list = MModule.split2(
			delim,
			(await parser.CurrentState.Arguments["0"].ParsedMessage())!);

		return new CallState(list.Length.ToString());
	}

	[SharpFunction(Name = "VDIM", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vdim(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "LINSERT", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> insert(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}