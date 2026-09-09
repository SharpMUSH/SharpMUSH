using SharpMUSH.Library.Utilities;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Numerics;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// SharpMUSH Implementation Status: 100%
/// </summary>
public partial class Functions
{
	/// <summary>
	/// Base 64 characters for conversion.
	/// </summary>
	private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

	/// <summary>
	/// Base 36 characters for conversion, for PennMUSH compatibility.
	/// </summary>
	private const string Chars36 = "0123456789abcdefghijklmnopqrstuvwxyz";

	[SharpFunction(Name = "baseconv", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "from-base", "to-base"])]
	public ValueTask<CallState> BaseConv(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var numbers = NumericEvaluation.For(parser);
		var args = parser.CurrentState.ArgumentsOrdered;
		var input = args["0"].Message!.ToPlainText();
		var fromBaseStr = args["1"].Message!.ToPlainText();
		var toBaseStr = args["2"].Message!.ToPlainText();

		if (!numbers.TryInt32(fromBaseStr, out var fromBase))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integers);
		}

		if (!numbers.TryInt32(toBaseStr, out var toBase))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integers);
		}

		if (fromBase is < 2 or > 64)
			return ValueTask.FromResult<CallState>(new(string.Format(ErrorMessages.Returns.BaseArgRange, 1)));

		if (toBase is < 2 or > 64)
			return ValueTask.FromResult<CallState>(new(string.Format(ErrorMessages.Returns.BaseArgRange, 2)));

		var fromBaseChars = fromBase <= 36 ? Chars36 : Chars;
		var toBaseChars = toBase <= 36 ? Chars36 : Chars;

		// Handle negative sign for bases <= 36 (where '-' is not a valid digit)
		var isNegative = false;
		if (fromBase <= 36 && input.StartsWith('-'))
		{
			isNegative = true;
			input = input[1..];
		}

		// Normalize standard Base64 characters (+/) to URL-safe (-_) for bases > 36
		if (fromBase > 36)
		{
			input = input.Replace('+', '-').Replace('/', '_');
		}

		if (input.Length == 0)
		{
			return ValueTask.FromResult<CallState>(new(ErrorMessages.Returns.MalformedNumber));
		}

		// The digit values, each checked once against the source base; a fold over them is the number.
		var number = BigInteger.Zero;
		foreach (var c in input)
		{
			var digit = fromBaseChars.IndexOf(c);
			if (digit < 0 || digit >= fromBase)
			{
				return ValueTask.FromResult<CallState>(new(ErrorMessages.Returns.MalformedNumber));
			}

			number = number * fromBase + digit;
		}

		if (toBase == 10)
		{
			var numStr = number.ToString();
			return ValueTask.FromResult<CallState>(new(isNegative && number != 0 ? "-" + numStr : numStr));
		}

		// Digits come out least significant first, so they fill a buffer from its end: a base-2 result
		// needs at most six digits per base-64 source digit, plus the sign.
		var capacity = input.Length * 6 + 2;
		Span<char> digits = capacity <= 512 ? stackalloc char[capacity] : new char[capacity];
		var start = digits.Length;
		while (number > 0)
		{
			digits[--start] = toBaseChars[(int)(number % toBase)];
			number /= toBase;
		}

		if (start == digits.Length)
		{
			digits[--start] = '0';
		}

		if (isNegative && toBase <= 36)
		{
			digits[--start] = '-';
		}

		return ValueTask.FromResult<CallState>(new(new string(digits[start..])));
	}

	[SharpFunction(Name = "band",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public ValueTask<CallState> BAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x & y);

	[SharpFunction(Name = "bnand", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer1", "integer2"])]
	public ValueTask<CallState> BNand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x & ~y);

	[SharpFunction(Name = "bnot", MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer"])]
	public ValueTask<CallState> BNot(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateUnsignedInteger(parser, x => ~x);

	[SharpFunction(Name = "bor",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public ValueTask<CallState> Bor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x | y);

	[SharpFunction(Name = "bxor",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public ValueTask<CallState> BXor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x ^ y);

	[SharpFunction(Name = "shr", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer", "positions"])]
	public ValueTask<CallState> ShR(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x >> (int)(y & 63));

	[SharpFunction(Name = "shl", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer", "positions"])]
	public ValueTask<CallState> ShL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x << (int)(y & 63));
}