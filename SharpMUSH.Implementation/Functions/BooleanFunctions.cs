using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions
{
	public static partial class Functions
	{
		[SharpFunction(Name = "and", Flags = FunctionFlags.Regular)]
		public static CallState And(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).All(Predicates.Truthy) 
				? "1" 
				: "0");

		[SharpFunction(Name = "cand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments
					.Select(x => x.Message!)
					.All(m => Predicates.Truthy(parser.FunctionParse(m).AsTask().Result!.Message!)) 
				? "0" 
				: "1");

		[SharpFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(m => Predicates.Truthy(parser.FunctionParse(m).AsTask().Result!.Message!)) 
				? "1" 
				: "0");

		[SharpFunction(Name = "eq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Eq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.All(x => x.Message == parser.CurrentState.Arguments[0].Message) 
				? "0" 
				: "1");

		[SharpFunction(Name = "gt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 > pair.Item2);

		[SharpFunction(Name = "gte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gte(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 >= pair.Item2);

		[SharpFunction(Name = "lt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 < pair.Item2);

		[SharpFunction(Name = "lte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lte(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 <= pair.Item2);

		[SharpFunction(Name = "nand", Flags = FunctionFlags.Regular)]
		public static CallState Nand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(Predicates.Falsey) ? "1" : "0");

		[SharpFunction(Name = "cnand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState CNand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(m => Predicates.Falsey(parser.FunctionParse(m).AsTask().Result!.Message!)) ? "0" : "1");

		[SharpFunction(Name = "neq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Neq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Any(x => x.Message == parser.CurrentState.Arguments[0].Message) ? "0" : "1");

		[SharpFunction(Name = "nor", Flags = FunctionFlags.Regular)]
		public static CallState Nor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).All(Predicates.Falsey) ? "1" : "0");

		[SharpFunction(Name = "ncor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState NCor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).All(m => Predicates.Falsey(parser.FunctionParse(m).AsTask().Result!.Message!)) ? "1" : "0");

		[SharpFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState Not(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(Predicates.Falsey(parser.CurrentState.Arguments[0].Message!) ? "1" : "0");

		[SharpFunction(Name = "or", Flags = FunctionFlags.Regular)]
		public static CallState Or(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(Predicates.Truthy) ? "1" : "0");

		[SharpFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 1)]
		public static CallState T(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(Predicates.Truthy(parser.CurrentState.Arguments.FirstOrDefault()?.Message!) ? "1" : "0");

		[SharpFunction(Name = "xor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Xor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Where(Predicates.Truthy).Count() == 1 ? "1" : "0");
	}
}