using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
{
	public partial class Functions
	{
		private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz+/";

		[PennFunction(Name = "baseconv", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState BaseConv(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateIntegerAndEvaluate(args[1..], (int[]x) => 
				{
					var input = MModule.plainText(args[0].Message!);
					var fromBase = x[1];
					var toBase = x[2];

					if (fromBase < 2 || fromBase > 64)
						return MModule.single("#-1 fromBase must be between 2 and 64.");

					if (toBase < 2 || toBase > 64)
						return MModule.single("#-1 toBase must be between 2 and 64.");

					// Validate input according to fromBase
					foreach (char c in input)
					{
						if (Chars.IndexOf(c) >= fromBase)
							return MModule.single("Invalid character in the input for the specified fromBase.");
					}

					// Convert input to base 10
					System.Numerics.BigInteger number = System.Numerics.BigInteger.Zero;
					foreach (char c in input)
					{
						number = number * fromBase + Chars.IndexOf(c);
					}

					// Directly return the number if toBase is 10
					if (toBase == 10)
						return MModule.single(number.ToString());

					// Convert from base 10 to the desired base
					string result = string.Empty;
					while (number > 0)
					{
						result = Chars[(int)(number % toBase)] + result;
						number /= toBase;
					}

					return MModule.single(result == string.Empty ? "0" : result);
				});

		[PennFunction(Name = "band", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState BAnd(Parser _1, PennFunctionAttribute _2, params CallState[] args)
	=> ValidateIntegerAndAggregate(args, (x, y) => x & y);

		[PennFunction(Name = "bnand", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState BNand(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateIntegerAndAggregate(args, (x, y) => ~(x & y));

		[PennFunction(Name = "bnot", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState BNot(Parser _1, PennFunctionAttribute _2, params CallState[] args)
					=> ValidateIntegerAndEvaluate(args, x => ~x);

		[PennFunction(Name = "bor", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Bor(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateIntegerAndAggregate(args, (x, y) => x | y);

		[PennFunction(Name = "bxor", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState BXor(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateIntegerAndAggregate(args, (x, y) => x ^ y);

		[PennFunction(Name = "shr", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ShR(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateIntegerAndAggregate(args, (x, y) => x >> y);

		[PennFunction(Name = "shl", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ShL(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateIntegerAndAggregate(args, (x, y) => x << y);
	}
}