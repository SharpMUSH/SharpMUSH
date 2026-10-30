using SharpMUSH.Library.Utilities;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Globalization;
using System.Numerics;
using System.Text;
using SharpMUSH.Implementation.Definitions;

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
		var input = args["0"].Message.ToPlainText();
		var fromBaseStr = args["1"].Message.ToPlainText();
		var toBaseStr = args["2"].Message.ToPlainText();

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

		// Each character's value as a digit of the source base, or -1 where it has none.
		var digits = input.Select(c => fromBaseChars.IndexOf(c)).ToArray();
		if (digits.Any(digit => digit < 0 || digit >= fromBase))
		{
			return ValueTask.FromResult<CallState>(new(ErrorMessages.Returns.MalformedNumber));
		}

		var number = digits.Aggregate(BigInteger.Zero, (total, digit) => total * fromBase + digit);

		if (toBase == 10)
		{
			var numStr = number.ToString();
			return ValueTask.FromResult<CallState>(new(isNegative && number != 0 ? "-" + numStr : numStr));
		}

		// Output digits come out least significant first, and how many there are is only known once
		// the divisions have run, so they fill a buffer from its end: a base-2 result needs at most six
		// digits per base-64 source digit, plus the sign.
		var capacity = input.Length * 6 + 2;
		Span<char> output = capacity <= 512 ? stackalloc char[capacity] : new char[capacity];
		var start = output.Length;
		while (number > 0)
		{
			output[--start] = toBaseChars[(int)(number % toBase)];
			number /= toBase;
		}

		if (start == output.Length)
		{
			output[--start] = '0';
		}

		if (isNegative && toBase <= 36)
		{
			output[--start] = '-';
		}

		return ValueTask.FromResult<CallState>(new(new string(output[start..])));
	}

	/// <summary>
	/// The digits TinyMUX and RhostMUSH give pack() and unpack(). Every one of them may appear in an attribute
	/// name, which is what the radix-64 form is for.
	/// </summary>
	private const string PackChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz@$";

	private const NumberStyles PackInteger = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign;

	private const string PackRadixRange = "#-1 RADIX MUST BE A NUMBER BETWEEN 2 and 64";

	/// <summary>
	/// TinyMUX's and RhostMUSH's pack(): <c>&lt;number&gt;</c> written in <c>&lt;radix&gt;</c> (64 when omitted).
	/// A true third argument writes baseconv()'s digits instead.
	/// </summary>
	[SharpFunction(Name = "pack", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["number", "radix", "baseconv digits"])]
	public ValueTask<CallState> Pack(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TinyMUX's is_integer: an empty argument is not a number, whatever null_eq_zero says. The number has no
		// size limit, so whatever unpack() reads, pack() writes back.
		var args = parser.CurrentState.ArgumentsOrdered;
		if (!BigInteger.TryParse(args["0"].Message.ToPlainText(), PackInteger, CultureInfo.InvariantCulture, out var number))
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		var radix = 64;
		if (args.TryGetValue("1", out var radixArg)
			&& !int.TryParse(radixArg.Message.ToPlainText(), PackInteger, CultureInfo.InvariantCulture, out radix))
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		if (radix is < 2 or > 64)
			return ValueTask.FromResult<CallState>(PackRadixRange);
		var pennDigits = args.TryGetValue("2", out var digitsArg) && digitsArg.Message.Truthy(parser);
		return ValueTask.FromResult<CallState>(PackNumber(number, radix, pennDigits));
	}

	private static string PackNumber(BigInteger number, int radix, bool pennDigits)
	{
		if (radix == 10) return number.ToString();
		// baseconv()'s radix 63 and 64 use '-' as a digit, so a sign has nothing to be written with. pack()'s own
		// digits have no '-', and RhostMUSH writes the sign in every radix.
		if (number.Sign < 0 && pennDigits && radix >= 63) return "#-1 NEGATIVE NUMBER IS NOT REPRESENTABLE IN OUTPUT RADIX";
		var digits = pennDigits ? radix <= 36 ? Chars36 : Chars : PackChars;
		var magnitude = BigInteger.Abs(number);
		var output = new StringBuilder();
		do
		{
			output.Insert(0, digits[(int)(magnitude % radix)]);
			magnitude /= radix;
		} while (magnitude > 0);
		if (number.Sign < 0) output.Insert(0, '-');
		return output.ToString();
	}

	/// <summary>
	/// TinyMUX's and RhostMUSH's unpack(): <c>&lt;string&gt;</c> read as a number in <c>&lt;radix&gt;</c> (64 when
	/// omitted), answered in decimal. A true third argument reads baseconv()'s digits instead. A character that is
	/// not a digit of the radix is an error, as in TinyMUX; RhostMUSH stops reading at it instead.
	/// </summary>
	[SharpFunction(Name = "unpack", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string", "radix", "baseconv digits"])]
	public ValueTask<CallState> Unpack(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var radix = 64;
		if (args.TryGetValue("1", out var radixArg)
			&& (!int.TryParse(radixArg.Message.ToPlainText(), PackInteger, CultureInfo.InvariantCulture, out radix) || radix is < 2 or > 64))
			return ValueTask.FromResult<CallState>(PackRadixRange);
		var pennDigits = args.TryGetValue("2", out var digitsArg) && digitsArg.Message.Truthy(parser);
		return ValueTask.FromResult<CallState>(UnpackNumber(args["0"].Message.ToPlainText(), radix, pennDigits)
			is BigInteger number ? number.ToString() : "#-1 NUMBER IS NOT VALID FOR INPUT RADIX");
	}

	private static BigInteger? UnpackNumber(string text, int radix, bool pennDigits)
	{
		var input = text.AsSpan().Trim(" \t\r\n\v\f");
		// baseconv()'s radix 63 and 64 count '-' as a digit, and its radix 64 also reads '+' and '/'.
		var signed = !pennDigits || radix < 63;
		var negative = signed && input.StartsWith("-");
		if (signed && (negative || input.StartsWith("+"))) input = input[1..];
		var digits = pennDigits ? radix <= 36 ? Chars36 : Chars : PackChars;
		var number = BigInteger.Zero;
		foreach (var c in input)
		{
			var digit = pennDigits && radix == 64 && c is '+' or '/' ? c == '+' ? 62 : 63 : digits.IndexOf(c);
			if (digit < 0 || digit >= radix) return null;
			number = number * radix + digit;
		}
		return negative ? -number : number;
	}

	[SharpFunction(Name = "band", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public ValueTask<CallState> BAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x & y);

	[SharpFunction(Name = "bnand", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer1", "integer2"])]
	public ValueTask<CallState> BNand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x & ~y);

	[SharpFunction(Name = "bnot", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer"])]
	public ValueTask<CallState> BNot(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateUnsignedInteger(parser, x => ~x);

	[SharpFunction(Name = "bor", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public ValueTask<CallState> Bor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateUnsignedIntegers(parser, (x, y) => x | y);

	[SharpFunction(Name = "bxor", MinArgs = 1, MaxArgs = int.MaxValue,
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