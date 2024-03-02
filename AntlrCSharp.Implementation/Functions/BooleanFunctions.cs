using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "and", Flags = FunctionFlags.Regular)]
		public static CallState And(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).All(Predicates.Truthy) ? "1" : "0");

		[PennFunction(Name = "cand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cand(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).All(m => Predicates.Truthy(parser.FunctionParse(m.ToString())!.Message!)) ? "0" : "1");

		[PennFunction(Name = "nand", Flags = FunctionFlags.Regular)]
		public static CallState Nand(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Any(Predicates.Falsey) ? "1" : "0");

		[PennFunction(Name = "cnand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cnand(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Any(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "0" : "1");

		[PennFunction(Name = "or", Flags = FunctionFlags.Regular)]
		public static CallState Or(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Any(Predicates.Truthy) ? "1" : "0");

		[PennFunction(Name = "nor", Flags = FunctionFlags.Regular)]
		public static CallState Nor(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).All(Predicates.Falsey) ? "1" : "0");

		[PennFunction(Name = "ncor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Ncor(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).All(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[PennFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cor(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Any(m => Predicates.Truthy(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[PennFunction(Name = "xor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Xor(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Where(Predicates.Truthy).Count() == 1 ? "1" : "0");

		[PennFunction(Name = "eq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Eq(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.All(x => x.Message == args[0].Message) ? "0" : "1");

		[PennFunction(Name = "neq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Neq(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Any(x => x.Message == args[0].Message) ? "0" : "1");

		[PennFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState Not(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(Predicates.Falsey(args[0].Message!) ? "1" : "0");

		[PennFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 1)]
		public static CallState T(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(Predicates.Truthy(args.FirstOrDefault()?.Message!) ? "1" : "0");
	}
}
