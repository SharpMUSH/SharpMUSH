﻿using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz+/";

	[SharpFunction(Name = "baseconv", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> BaseConv(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValidateIntegerAndEvaluate(parser.CurrentState.Arguments[1..], (IEnumerable<int> x) =>
		{
			var input = MModule.plainText(parser.CurrentState.Arguments[0].Message!);
			var fromBase = x.ElementAtOrDefault(1);
			var toBase = x.ElementAtOrDefault(2);

			if (fromBase < 2 || fromBase > 64)
				return MModule.single("#-1 Argument 1 must be between 2 and 64.");

			if (toBase < 2 || toBase > 64)
				return MModule.single("#-1 Argument 2 must be between 2 and 64.");

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

	[SharpFunction(Name = "band", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> BAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AggregateIntegers(parser.CurrentState.Arguments, (x, y) => x & y);

	[SharpFunction(Name = "bnand", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> BNand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AggregateIntegers(parser.CurrentState.Arguments, (x, y) => ~(x & y));

	[SharpFunction(Name = "bnot", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> BNot(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateInteger(parser.CurrentState.Arguments, x => ~x);

	[SharpFunction(Name = "bor", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Bor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AggregateIntegers(parser.CurrentState.Arguments, (x, y) => x | y);

	[SharpFunction(Name = "bxor", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> BXor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AggregateIntegers(parser.CurrentState.Arguments, (x, y) => x ^ y);

	[SharpFunction(Name = "shr", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ShR(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AggregateIntegers(parser.CurrentState.Arguments, (x, y) => x >> y);

	[SharpFunction(Name = "shl", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ShL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AggregateIntegers(parser.CurrentState.Arguments, (x, y) => x << y);
}