using System.Globalization;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// TinyMUX's integer list functions: <c>lband()</c>, <c>lbor()</c>, <c>lbxor()</c> and <c>limath()</c>. Each
/// reads a list of 64-bit integers, refuses any item that is not one, and wraps around on overflow as
/// TinyMUX does.
/// </summary>
public partial class Functions
{
	[SharpFunction(Name = "lband", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListBitwiseAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(FoldIntegerList(parser, 0, 1, -1L, (x, y) => x & y));

	[SharpFunction(Name = "lbor", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListBitwiseOr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(FoldIntegerList(parser, 0, 1, 0L, (x, y) => x | y));

	[SharpFunction(Name = "lbxor", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListBitwiseXor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(FoldIntegerList(parser, 0, 1, 0L, (x, y) => x ^ y));

	/// <summary>
	/// The list in argument <paramref name="listArgument"/>, split on the delimiter in
	/// <paramref name="delimiterArgument"/>, folded from <paramref name="identity"/>. No list, or an empty one,
	/// is the identity.
	/// </summary>
	private static CallState FoldIntegerList(IMUSHCodeParser parser, int listArgument, int delimiterArgument,
		long identity, Func<long, long, long> fold)
		=> ReadIntegerList(parser, listArgument, delimiterArgument) switch
		{
			long[] values => new CallState(values.Aggregate(identity, fold).ToString(CultureInfo.InvariantCulture)),
			Error<string> error => new CallState(error.Value)
		};

	/// <summary>The integers of a list argument, or the error for an item that is not one.</summary>
	private static Result<long[]> ReadIntegerList(IMUSHCodeParser parser, int listArgument, int delimiterArgument)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (args.Count <= listArgument || args[listArgument.ToString(CultureInfo.InvariantCulture)].Message is not { } list
			|| list.ToPlainText().Length == 0)
		{
			return Array.Empty<long>();
		}

		var values = new List<long>();
		foreach (var item in MushText.SplitList(ArgHelpers.Delimiter(args, delimiterArgument), list))
		{
			if (!ArgHelpers.TryStrictInteger(item.ToPlainText(), out long value))
			{
				return new Error<string>(ErrorMessages.Returns.Integers);
			}

			values.Add(value);
		}

		return values.ToArray();
	}

	/// <summary>
	/// TinyMUX's <c>limath()</c>: <c>add</c> (or <c>sum</c>), <c>sub</c>, <c>mul</c>, <c>div</c>, <c>mod</c>,
	/// <c>min</c>, <c>max</c> or <c>median</c> over a list of integers. Division truncates, a remainder takes the
	/// divisor's sign as <c>mod()</c> does, and the median of an even count is the lower half of the two middle
	/// values' sum, rounded down. An empty list is 0.
	/// </summary>
	[SharpFunction(Name = "limath", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["operation", "list", "delimiter"])]
	public ValueTask<CallState> ListIntegerMath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var operation = parser.CurrentState.Arguments["0"].Message?.ToPlainText().ToLowerInvariant() ?? string.Empty;
		return ValueTask.FromResult(ReadIntegerList(parser, 1, 2) switch
		{
			long[] { Length: 0 } => new CallState("0"),
			long[] values => IntegerListMath(operation, values),
			Error<string> error => new CallState(error.Value)
		});
	}

	private static CallState IntegerListMath(string operation, long[] values)
	{
		var result = operation switch
		{
			"add" or "sum" => values.Aggregate(0L, (x, y) => unchecked(x + y)),
			"sub" => values.Skip(1).Aggregate(values[0], (x, y) => unchecked(x - y)),
			"mul" => values.Aggregate(1L, (x, y) => unchecked(x * y)),
			"div" or "mod" when values.Skip(1).Contains(0L) => (long?)null,
			"div" => values.Skip(1).Aggregate(values[0], (x, y) => x == long.MinValue && y == -1 ? long.MinValue : x / y),
			"mod" => values.Skip(1).Aggregate(values[0], FlooredRemainder),
			"min" => values.Min(),
			"max" => values.Max(),
			"median" => IntegerMedian(values),
			_ => (long?)null
		};

		return result switch
		{
			long value => new CallState(value.ToString(CultureInfo.InvariantCulture)),
			null when operation is "div" or "mod" => new CallState(ErrorMessages.Returns.DivisionByZero),
			null => new CallState(ErrorMessages.Returns.UnknownOperation)
		};
	}

	/// <summary>The remainder with the sign of <paramref name="divisor"/>.</summary>
	private static long FlooredRemainder(long dividend, long divisor)
	{
		if (divisor == -1) return 0;
		var remainder = dividend % divisor;
		return remainder != 0 && (remainder < 0) != (divisor < 0) ? remainder + divisor : remainder;
	}

	private static long IntegerMedian(long[] values)
	{
		var sorted = values.Order().ToArray();
		var middle = sorted.Length / 2;
		if (sorted.Length % 2 == 1) return sorted[middle];

		var (low, high) = (sorted[middle - 1], sorted[middle]);
		return unchecked(low + (long)((ulong)(high - low) / 2));
	}
}
