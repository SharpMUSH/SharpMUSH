using System.Globalization;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's list maths: <c>ladd()</c>, <c>lsub()</c>, <c>lmul()</c>, <c>ldiv()</c>, <c>lavg()</c>,
/// <c>lmax()</c>, <c>lmin()</c>, and the booleans <c>land()</c>, <c>lor()</c>, <c>lxor()</c>,
/// <c>lnor()</c> and <c>lxnor()</c>. Each takes a list and an optional delimiter, reads every item as
/// the number it starts with (zero if none), and answers 0 for an empty list. The arithmetic ones are
/// lmath() under another name.
/// </summary>
public partial class Functions
{
	[SharpFunction(Name = "ladd", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListArithmeticAsync(parser, "add");

	[SharpFunction(Name = "lsub", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListSubtract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListArithmeticAsync(parser, "sub");

	[SharpFunction(Name = "lmul", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListMultiply(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListArithmeticAsync(parser, "mul");

	/// <summary>The first number divided by each of the others in turn; a divisor of zero is passed over.</summary>
	[SharpFunction(Name = "ldiv", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListDivide(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListArithmeticAsync(parser, "fdiv", numbers => numbers.Take(1).Concat(numbers.Skip(1).Where(number => number != 0m)));

	[SharpFunction(Name = "lavg", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListAverage(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListArithmeticAsync(parser, "mean");

	[SharpFunction(Name = "lmax", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListMaximum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListArithmeticAsync(parser, "max");

	[SharpFunction(Name = "lmin", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListMinimum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListArithmeticAsync(parser, "min");

	[SharpFunction(Name = "land", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListBooleanAsync(parser, "and");

	[SharpFunction(Name = "lor", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListOr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListBooleanAsync(parser, "or");

	/// <summary>Each step is true when exactly one of the result so far and the next number is.</summary>
	[SharpFunction(Name = "lxor", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListXor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(ListBooleanFold(parser, (sofar, next) => (sofar != 0) != (next != 0)));

	/// <summary>Each step is true when neither the result so far nor the next number is.</summary>
	[SharpFunction(Name = "lnor", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListNor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(ListBooleanFold(parser, (sofar, next) => sofar == 0 && next == 0));

	/// <summary>Each step is true when the result so far and the next number are both true or both false.</summary>
	[SharpFunction(Name = "lxnor", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ListXnor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(ListBooleanFold(parser, (sofar, next) => (sofar != 0) == (next != 0)));

	/// <summary>
	/// The items of a list-maths list: split on the delimiter (a space if none or empty, which drops
	/// empty items), each item's text. Another delimiter keeps an empty item, which counts as 0.
	/// </summary>
	private static string[] ListMathItems(IMUSHCodeParser parser)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = OptionalArgument(args, 1, MarkupText.Space);
		var list = args["0"].Message;
		return list.Length == 0
			? []
			: Array.ConvertAll(MushText.SplitList(delimiter, list), item => item.ToPlainText());
	}

	private async ValueTask<CallState> ListArithmeticAsync(IMUSHCodeParser parser, string operation,
		Func<IEnumerable<decimal>, IEnumerable<decimal>>? select = null)
	{
		var numbers = ListMathItems(parser).Select(item => RhostNumbers.LeadingNumber(item));
		var operands = (select?.Invoke(numbers) ?? numbers)
			.Select(number => MarkupText.Plain(number.ToString(CultureInfo.InvariantCulture)))
			.ToArray();

		return operands.Length == 0
			? new CallState("0")
			: await ApplyLMathAsync(parser, LMathOperations[operation], operands);
	}

	private async ValueTask<CallState> ListBooleanAsync(IMUSHCodeParser parser, string operation)
	{
		var operands = Array.ConvertAll(ListMathItems(parser),
			item => MarkupText.Plain(RhostNumbers.LeadingInteger(item).ToString(CultureInfo.InvariantCulture)));

		return operands.Length == 0
			? new CallState("0")
			: await ApplyLMathAsync(parser, LMathOperations[operation], operands);
	}

	/// <summary>
	/// A left fold over the list's whole numbers: the first is taken as it is, and each one after it
	/// combines with the result so far into 1 or 0. A list of one number is that number.
	/// </summary>
	private static CallState ListBooleanFold(IMUSHCodeParser parser, Func<int, int, bool> step)
	{
		var numbers = Array.ConvertAll(ListMathItems(parser), item => RhostNumbers.LeadingInteger(item));
		if (numbers.Length == 0) return new CallState("0");

		var result = numbers[0];
		foreach (var next in numbers.Skip(1))
		{
			result = step(result, next) ? 1 : 0;
		}

		return new CallState(result.ToString(CultureInfo.InvariantCulture));
	}
}
