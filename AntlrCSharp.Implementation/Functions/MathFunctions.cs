using AntlrCSharp.Implementation.Constants;

namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "add", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Add(Parser _1, PennMUSHParser.FunctionContext context, PennFunctionAttribute _2, params CallState[] args)
		{
			var doubles = args.Select(x =>
				(
					IsDouble: decimal.TryParse(string.Join("", x?.Message), out var b),
					Double: b
				));

			return doubles.Any(x => !x.IsDouble)
					? new CallState(Message: Errors.ErrorNumbers, context.Depth())
					: new CallState(Message: doubles.Sum(x => x.Double).ToString(), context.Depth());
		}
	}
}
