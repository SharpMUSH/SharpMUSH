using MoreLinq.Extensions;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

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

	[SharpFunction(Name = "FIRST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> first(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var argCount = parser.CurrentState.Arguments.Count;

		var delim = argCount > 1
			? parser.CurrentState.Arguments[1].Message!
			: MModule.single(" ");

		var listArg = parser.CurrentState.Arguments[0].Message;
		var list = MModule.split(delim.ToString(), listArg);
		var first = list?.FirstOrDefault() ?? MModule.empty();

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
		var argCount = parser.CurrentState.Arguments.Count;

		var listArg = await parser.FunctionParse(parser.CurrentState.Arguments[0].Message!);
		var delim = argCount > 2
			? (await parser.FunctionParse(parser.CurrentState.Arguments[2].Message!))!.Message!
			: MModule.single(" ");
		var sep = argCount > 3
			? (await parser.FunctionParse(parser.CurrentState.Arguments[3].Message!))!.Message!
			: delim;

		var list = MModule.split(MModule.plainText(delim), listArg!.Message);

		var result = new List<MString>();

		var wrappedIteration = new IterationWrapper<MString> { Value = MModule.empty() };
		parser.CurrentState.IterationRegisters.Push(wrappedIteration);

		foreach (var item in list)
		{
			wrappedIteration.Value = item!;
			wrappedIteration.Iteration++;
			var parsed = await parser.FunctionParse(parser.CurrentState.Arguments[1].Message!);
			result.Add(parsed!.Message!);
			result.Add(sep);

			if (wrappedIteration.Break)
			{
				break;
			}
		}

		parser.CurrentState.IterationRegisters.Pop();

		return new CallState(result.Count > 0
			? MModule.multiple(result[..^1])
			: MModule.empty());
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
		var argVal = parser.CurrentState.Arguments[0].Message!.ToString();
		var iterDepth = string.IsNullOrWhiteSpace(argVal)
			? "0"
			: argVal;
		var iterNumber = int.Parse(iterDepth);
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

	[SharpFunction(Name = "LAST", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> last(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var argCount = parser.CurrentState.Arguments.Count;

		var delim = argCount > 1
			? parser.CurrentState.Arguments[1].Message!
			: MModule.single(" ");

		var listArg = parser.CurrentState.Arguments[0].Message;
		var list = MModule.split(delim.ToString(), listArg);
		var last = list?.LastOrDefault() ?? MModule.empty();

		return ValueTask.FromResult(new CallState(last));
	}

	[SharpFunction(Name = "LDELETE", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ldelete(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MAP", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> map(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "MATCH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> match(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
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
		var argCount = parser.CurrentState.Arguments.Count;

		var delim = argCount > 1
			? parser.CurrentState.Arguments[1].Message!
			: MModule.single(" ");

		var listArg = parser.CurrentState.Arguments[0].Message;
		var list = MModule.split(delim.ToString(), listArg);
		var listElements = list.Length;
		var delimiters = Enumerable.Repeat(delim, Math.Max(0, listElements - 2));
		var last = MModule.multiple(list?.Skip(1).Interleave(delimiters));

		return ValueTask.FromResult(new CallState(last));
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

	[SharpFunction(Name = "REVWORDS", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> revwords(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
	public static ValueTask<CallState> unique(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WORDPOS", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> wordpos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "WORDS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> words(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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