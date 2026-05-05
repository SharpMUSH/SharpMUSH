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
	public static ValueTask<CallState> BaseConv(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var input = MModule.plainText(parser.CurrentState.ArgumentsOrdered.ElementAt(0).Value.Message!);
		var fromBaseStr = MModule.plainText(parser.CurrentState.ArgumentsOrdered.ElementAt(1).Value.Message!);
		var toBaseStr = MModule.plainText(parser.CurrentState.ArgumentsOrdered.ElementAt(2).Value.Message!);

		// Parse the base arguments as integers
		if (!int.TryParse(ArgHelpers.EmptyStringToZero(fromBaseStr), out var fromBase))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integers);
		}

		if (!int.TryParse(ArgHelpers.EmptyStringToZero(toBaseStr), out var toBase))
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

		// Validate input according to fromBase
		if (input.Length == 0 || input.Any(c => fromBaseChars.IndexOf(c) < 0 || fromBaseChars.IndexOf(c) >= fromBase))
		{
			return ValueTask.FromResult<CallState>(new(ErrorMessages.Returns.MalformedNumber));
		}

		// Convert input to base 10
		var number = input.Aggregate(BigInteger.Zero,
			(current, c) => current * fromBase + fromBaseChars.IndexOf(c));

		// Directly return the number if toBase is 10
		if (toBase == 10)
		{
			var numStr = number.ToString();
			return ValueTask.FromResult<CallState>(new(isNegative && number != 0 ? "-" + numStr : numStr));
		}

		// Convert from base 10 to the desired base
		var result = string.Empty;
		while (number > 0)
		{
			result = toBaseChars[(int)(number % toBase)] + result;
			number /= toBase;
		}

		var output = result == string.Empty ? "0" : result;
		if (isNegative && toBase <= 36)
			output = "-" + output;
		return ValueTask.FromResult<CallState>(new(output));
	}

	[SharpFunction(Name = "band",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public static ValueTask<CallState> BAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (x, y) => x & y);

	[SharpFunction(Name = "bnand",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer1", "integer2"])]
	public static ValueTask<CallState> BNand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (x, y) => ~(x & y));

	[SharpFunction(Name = "bnot", MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer"])]
	public static ValueTask<CallState> BNot(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateInteger(parser.CurrentState.ArgumentsOrdered, x => ~x);

	[SharpFunction(Name = "bor",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public static ValueTask<CallState> Bor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (x, y) => x | y);

	[SharpFunction(Name = "bxor",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer..."])]
	public static ValueTask<CallState> BXor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (x, y) => x ^ y);

	[SharpFunction(Name = "shr", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer", "positions"])]
	public static ValueTask<CallState> ShR(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (x, y) => x >> y);

	[SharpFunction(Name = "shl", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.PositiveIntegersOnly, ParameterNames = ["integer", "positions"])]
	public static ValueTask<CallState> ShL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (x, y) => x << y);
}