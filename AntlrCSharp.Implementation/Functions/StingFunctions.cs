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

		[PennFunction(Name = "lit", Flags = FunctionFlags.Regular, MaxArgs = 1)]
		public static CallState Lit(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			throw new Exception("This should never get called. The FunctionParser should handle this.");
		}
	}
}
