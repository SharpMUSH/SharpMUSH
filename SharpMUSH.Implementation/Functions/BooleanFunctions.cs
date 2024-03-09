﻿using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "and", Flags = FunctionFlags.Regular)]
		public static CallState And(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).All(Predicates.Truthy) ? "1" : "0");

		[PennFunction(Name = "cand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cand(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).All(m => Predicates.Truthy(parser.FunctionParse(m.ToString())!.Message!)) ? "0" : "1");

		[PennFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cor(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).Any(m => Predicates.Truthy(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[PennFunction(Name = "eq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Eq(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.All(x => x.Message == parser.State.Peek().Arguments[0].Message) ? "0" : "1");

		[PennFunction(Name = "gt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gt(Parser parser, PennFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.State.Peek().Arguments, pair => pair.Item1 > pair.Item2);

		[PennFunction(Name = "gte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gte(Parser parser, PennFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.State.Peek().Arguments, pair => pair.Item1 >= pair.Item2);

		[PennFunction(Name = "lt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lt(Parser parser, PennFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.State.Peek().Arguments, pair => pair.Item1 < pair.Item2);

		[PennFunction(Name = "lte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lte(Parser parser, PennFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.State.Peek().Arguments, pair => pair.Item1 <= pair.Item2);

		[PennFunction(Name = "nand", Flags = FunctionFlags.Regular)]
		public static CallState Nand(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).Any(Predicates.Falsey) ? "1" : "0");

		[PennFunction(Name = "cnand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState CNand(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).Any(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "0" : "1");

		[PennFunction(Name = "neq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Neq(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Any(x => x.Message == parser.State.Peek().Arguments[0].Message) ? "0" : "1");

		[PennFunction(Name = "nor", Flags = FunctionFlags.Regular)]
		public static CallState Nor(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).All(Predicates.Falsey) ? "1" : "0");

		[PennFunction(Name = "ncor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState NCor(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).All(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[PennFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState Not(Parser parser, PennFunctionAttribute _2)
			=> new(Predicates.Falsey(parser.State.Peek().Arguments[0].Message!) ? "1" : "0");

		[PennFunction(Name = "or", Flags = FunctionFlags.Regular)]
		public static CallState Or(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).Any(Predicates.Truthy) ? "1" : "0");

		[PennFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState T(Parser parser, PennFunctionAttribute _2)
			=> new(Predicates.Truthy(parser.State.Peek().Arguments.FirstOrDefault()?.Message!) ? "1" : "0");

		[PennFunction(Name = "xor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Xor(Parser parser, PennFunctionAttribute _2)
			=> new(parser.State.Peek().Arguments.Select(x => x.Message!).Where(Predicates.Truthy).Count() == 1 ? "1" : "0");
	}
}