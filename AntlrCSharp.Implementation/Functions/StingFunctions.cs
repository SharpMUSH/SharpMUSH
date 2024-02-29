using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "strcat", Flags = FunctionFlags.Regular)]
		public static CallState Concat(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			return new CallState(string.Join("", args.Select(x => x.Message)));
		}
	}
}
