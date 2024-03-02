using AntlrCSharp.Implementation.Definitions;
using AntlrCSharp.Implementation.Tools;

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
		public static CallState CNand(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Any(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "0" : "1");

		[PennFunction(Name = "or", Flags = FunctionFlags.Regular)]
		public static CallState Or(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Any(Predicates.Truthy) ? "1" : "0");

		[PennFunction(Name = "nor", Flags = FunctionFlags.Regular)]
		public static CallState Nor(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).All(Predicates.Falsey) ? "1" : "0");

		[PennFunction(Name = "ncor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState NCor(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).All(m => Predicates.Falsey(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[PennFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cor(Parser parser, PennFunctionAttribute _2, params CallState[] args)
			=> new(args.Select(x => x.Message!).Any(m => Predicates.Truthy(parser.FunctionParse(m.ToString())!.Message!)) ? "1" : "0");

		[PennFunction(Name = "xor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Xor(Parser _1, PennFunctionAttribute _2, params CallState[] args)
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

		[PennFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState T(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> new(Predicates.Truthy(args.FirstOrDefault()?.Message!) ? "1" : "0");

		[PennFunction(Name = "gt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gt(Parser _1, PennFunctionAttribute _2, params CallState[] args) 
			=> ValidateDecimalAndEvaluatePairwise(args, pair => pair.Item1 > pair.Item2);

		[PennFunction(Name = "gte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Gte(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateDecimalAndEvaluatePairwise(args, pair => pair.Item1 >= pair.Item2);

		[PennFunction(Name = "lt", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lt(Parser _1, PennFunctionAttribute _2, params CallState[] args) 
			=> ValidateDecimalAndEvaluatePairwise(args, pair => pair.Item1 < pair.Item2);

		[PennFunction(Name = "lte", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Lte(Parser _1, PennFunctionAttribute _2, params CallState[] args) 
			=> ValidateDecimalAndEvaluatePairwise(args, pair => pair.Item1 <= pair.Item2);

		private static CallState ValidateDecimalAndEvaluatePairwise(this CallState[] args, Func<(decimal, decimal), bool> func)
		{
			if (args.Length < 2)
			{
				return new CallState(Message: Errors.ErrorTooFewArguments);
			}

			var doubles = args.Select(x =>
				(
					IsDouble: decimal.TryParse(string.Join("", MModule.plainText(x.Message)), out var b),
					Double: b
				)).ToList();

			return doubles.Any(x => !x.IsDouble)
					? new CallState(Message: Errors.ErrorNumbers)
					: new CallState(Message: doubles.Select(x => x.Double).Pairwise().Skip(1).SkipWhile(func).Any().ToString());
		}
	}
}
