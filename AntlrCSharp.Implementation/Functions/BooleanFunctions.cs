using AntlrCSharp.Implementation.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		[PennFunction(Name = "and", Flags = FunctionFlags.Regular)]
		public static CallState And(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			return new CallState(args.Select(x => x.Message!).All(Predicates.Truthy) ? "1" : "0");
		}

		[PennFunction(Name = "cand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState Cand(Parser parser, PennFunctionAttribute _2, params CallState[] args)
		{
			return new CallState(args.Select(x => x.Message!).Any(m => Predicates.Falsey(parser.FunctionParse(m)!.Message!)) ? "0" : "1");
		}

		[PennFunction(Name = "or", Flags = FunctionFlags.Regular)]
		public static CallState Or(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			return new CallState(args.Select(x => x.Message!).Any(Predicates.Truthy) ? "1" : "0");
		}


		[PennFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
		public static CallState COr(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			return new CallState(args.Select(x => x.Message!).Any(Predicates.Truthy) ? "1" : "0");
		}

		[PennFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
		public static CallState Not(Parser _1, PennFunctionAttribute _2, params CallState[] args)
		{
			return new CallState(Predicates.Falsey(args[0].Message!) ? "1" : "0");
		}
	}
}
