using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "strcat", Flags = FunctionFlags.Regular)]
		public static CallState Concat(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args
					.Select(x => x.Message)
					.Aggregate(MModule.concat));

		[PennFunction(Name = "cat", Flags = FunctionFlags.Regular)]
		public static CallState Cat(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args
					.Select(x => x.Message)
					.Aggregate((x,y) => MModule.concat2(x,MModule.single(" "),y)));

		[PennFunction(Name = "lit", Flags = FunctionFlags.Regular | FunctionFlags.NoParse, MaxArgs = 1)]
		public static CallState Lit(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			throw new Exception("This should never get called. The FunctionParser should handle this.");
		}
	}
}
