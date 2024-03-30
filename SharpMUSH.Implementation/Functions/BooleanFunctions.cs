﻿using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	public static partial class Functions
	{
		[SharpFunction(Name = "and", Flags = FunctionFlags.Regular)]
		public static CallState And(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).All(Predicates.Truthy) ? "1" : "0");

		[SharpFunction(Name = "cand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cand(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).All(m => Predicates.Truthy(parser.FunctionParse(m.ToString())!.Message!)) ? "0" : "1");

		[SharpFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cor(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(m => Predicates.Truthy(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[SharpFunction(Name = "eq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Eq(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.All(x => x.Message == parser.CurrentState.Arguments[0].Message) ? "0" : "1");

		[SharpFunction(Name = "gt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gt(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 > pair.Item2);

		[SharpFunction(Name = "gte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gte(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 >= pair.Item2);

		[SharpFunction(Name = "lt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lt(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 < pair.Item2);

		[SharpFunction(Name = "lte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lte(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 <= pair.Item2);

		[SharpFunction(Name = "nand", Flags = FunctionFlags.Regular)]
		public static CallState Nand(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(Predicates.Falsey) ? "1" : "0");

		[SharpFunction(Name = "cnand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState CNand(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "0" : "1");

		[SharpFunction(Name = "neq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
		public static CallState Neq(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Any(x => x.Message == parser.CurrentState.Arguments[0].Message) ? "0" : "1");

		[SharpFunction(Name = "nor", Flags = FunctionFlags.Regular)]
		public static CallState Nor(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).All(Predicates.Falsey) ? "1" : "0");

		[SharpFunction(Name = "ncor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState NCor(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).All(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[SharpFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState Not(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(Predicates.Falsey(parser.CurrentState.Arguments[0].Message!) ? "1" : "0");

		[SharpFunction(Name = "or", Flags = FunctionFlags.Regular)]
		public static CallState Or(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Any(Predicates.Truthy) ? "1" : "0");

		[SharpFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState T(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(Predicates.Truthy(parser.CurrentState.Arguments.FirstOrDefault()?.Message!) ? "1" : "0");

		[SharpFunction(Name = "xor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Xor(MUSHCodeParser parser, SharpFunctionAttribute _2)
			=> new(parser.CurrentState.Arguments.Select(x => x.Message!).Where(Predicates.Truthy).Count() == 1 ? "1" : "0");
	}
}