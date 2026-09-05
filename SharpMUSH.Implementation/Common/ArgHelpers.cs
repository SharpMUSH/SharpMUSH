using Mediator;
using OneOf;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Common;

public static partial class ArgHelpers
{
	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		MString defaultValue)
	{
		if (args.Count - 1 < item || item == 0 && string.IsNullOrEmpty(args[item.ToString()]?.Message?.ToString()) ||
				args[item.ToString()].Message?.ToString() is null)
		{
			return defaultValue;
		}

		return args[item.ToString()].Message!;
	}

	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		string defaultValue)
		=> NoParseDefaultNoParseArgument(args, item, MModule.single(defaultValue));

	public static async ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		MString defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		if (args.Count - 1 < item || MModule.getLength(args[item.ToString()].Message!) == 0)
		{
			return defaultValue;
		}

		return (await args[item.ToString()].ParsedMessage())!;
	}

	public static ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		string defaultValue)
		=> NoParseDefaultEvaluatedArgument(parser, item, MModule.single(defaultValue));

	public static async ValueTask<MString> EvaluatedDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		CallState defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		var parsedValue = (await args[item.ToString()].ParsedMessage())!;
		if (args.Count - 1 < item || MModule.getLength(parsedValue) == 0)
		{
			return (await defaultValue.ParsedMessage())!;
		}

		return parsedValue;
	}

	public static ValueTask<CallState> AggregateDecimals(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal, decimal> aggregateFunction)
	{
		var decimals = new List<decimal>();

		foreach (var arg in args)
		{
			var text = EmptyStringToZero(MModule.plainText(arg.Value.Message));
			if (!decimal.TryParse(text, out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}
			decimals.Add(value);
		}

		var result = decimals.Aggregate(aggregateFunction);

		return ValueTask.FromResult<CallState>(FormatDecimal(result));
	}

	/// <summary>
	/// Formats a decimal number to remove unnecessary trailing zeros and decimal point.
	/// E.g., 10.0 -> "10", 10.5 -> "10.5", 10.123 -> "10.123"
	/// </summary>
	private static string FormatDecimal(decimal value)
	{
		return value.ToString("0.##########", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Aggregates arguments as 64-bit unsigned integers, matching PennMUSH's UIVAL. The result is
	/// rendered signed, because PennMUSH renders it that way too: bnot(0) is -1, not 18446744073709551615.
	/// </summary>
	public static ValueTask<CallState> AggregateUnsignedIntegers(ImmutableSortedDictionary<string, CallState> args,
		Func<ulong, ulong, ulong> aggregateFunction)
	{
		var integers = new List<ulong>();

		foreach (var arg in args)
		{
			var text = EmptyStringToZero(MModule.plainText(arg.Value.Message));
			if (!ulong.TryParse(text, out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.UIntegers);
			}
			integers.Add(value);
		}

		var result = integers.Aggregate(aggregateFunction);
		return ValueTask.FromResult<CallState>(unchecked((long)result).ToString(CultureInfo.InvariantCulture));
	}

	/// <inheritdoc cref="AggregateUnsignedIntegers"/>
	public static ValueTask<CallState> EvaluateUnsignedInteger(ImmutableSortedDictionary<string, CallState> args,
		Func<ulong, ulong> func)
	{
		var text = EmptyStringToZero(MModule.plainText(args["0"].Message));
		if (!ulong.TryParse(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.UInteger);
		}

		return ValueTask.FromResult<CallState>(unchecked((long)func(value)).ToString(CultureInfo.InvariantCulture));
	}

	public static ValueTask<CallState> EvaluateDecimal(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal> func)
	{
		var text = EmptyStringToZero(MModule.plainText(args["0"].Message));
		if (!decimal.TryParse(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		var result = func(value);
		return ValueTask.FromResult<CallState>(FormatDecimal(result));
	}

	public static ValueTask<CallState> EvaluateDecimalToInteger(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, long> func)
	{
		var text = EmptyStringToZero(MModule.plainText(args["0"].Message));
		if (!decimal.TryParse(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		return ValueTask.FromResult<CallState>(func(value));
	}

	public static ValueTask<CallState> EvaluateDouble(ImmutableSortedDictionary<string, CallState> args,
		Func<double, double> func)
	{
		var text = EmptyStringToZero(MModule.plainText(args["0"].Message));
		if (!double.TryParse(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		return ValueTask.FromResult<CallState>(func(value));
	}

	public static string EmptyStringToZero(string input)
		=> string.IsNullOrEmpty(input) ? "0" : input;

	public static ValueTask<CallState> ValidateDecimalAndEvaluatePairwise(
		ImmutableSortedDictionary<string, CallState> args,
		Func<(decimal, decimal), bool> func)
	{
		if (args.Count < 2)
		{
			return ValueTask.FromResult(new CallState(Message: ErrorMessages.Returns.TooFewArguments));
		}

		var doubles = args.Select(x =>
		(
			IsDouble: decimal.TryParse(string.Join("", EmptyStringToZero(MModule.plainText(x.Value.Message))), out var b),
			Double: b
		)).ToList();

		if (doubles.Any(x => !x.IsDouble))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		var result = doubles.Select(x => x.Double).Pairwise().All(func);

		return new ValueTask<CallState>(result ? "1" : "0");
	}

	public static async ValueTask<bool> HasObjectFlags(SharpObject obj, SharpObjectFlag flag)
		=> await obj.Flags.Value
			.ContainsAsync(flag);

	public static async ValueTask<bool> HasObjectPowers(SharpObject obj, string power) =>
		await obj.Powers.Value
			.AnyAsync(x => x.Name == power || x.Alias == power);

	public static IEnumerable<OneOf<DBRef, string>> NameList(string list)
		=> NameListPattern().Matches(list).Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? OneOf<DBRef, string>.FromT0(HelperFunctions.ParseDbRef(x.Groups["DBRef"].Value).AsValue())
				: OneOf<DBRef, string>.FromT1(x.Groups["User"].Value));

	public static IEnumerable<string> NameListString(string list)
		=> NameListPattern().Matches(list).Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? HelperFunctions.ParseDbRef(x.Groups["DBRef"].Value).AsValue().ToString()
				: x.Groups["User"].Value);

	public static async ValueTask<CallState> ForHandleOrPlayer(IMUSHCodeParser parser, IMediator mediator,
		IConnectionService connectionService, ILocateService locateService, CallState value,
		Func<long, IConnectionService.ConnectionData, ValueTask<CallState>> handleFunc,
		Func<SharpPlayer, IConnectionService.ConnectionData, ValueTask<CallState>> playerFunc)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var valueText = MModule.plainText(value.Message);

		var isHandle = long.TryParse(valueText, out var handle);

		if (isHandle)
		{
			var handleData = connectionService.Get(handle);
			if (handleData is null) return new CallState("#-1 That handle is not connected.");

			return await handleFunc(handle, handleData);
		}

		var maybeFound =
			await locateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, valueText);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		var found = maybeFound.AsSharpObject.AsPlayer;
		var foundData = await connectionService.Get(found.Object.DBRef).FirstOrDefaultAsync();

		if (foundData is null) return new CallState("#-1 That player is not connected.");

		return await playerFunc(found, foundData);
	}

	/// <summary>
	/// Parses a room/object format string like "room/obj1 obj2" into a room name and list of object names.
	/// PennMUSH compatibility: Supports "room/obj1 obj2" format where message is sent to room excluding listed objects.
	/// </summary>
	/// <param name="input">The input string to parse</param>
	/// <returns>Tuple of (roomName, objectNames list) or null if no room/obj format detected</returns>
	public static (string roomName, List<string> objectNames)? ParseRoomObjectFormat(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return null;
		}

		var slashIndex = input.IndexOf('/');
		if (slashIndex < 0 || slashIndex == 0)
		{
			return null; // No room/obj format, or empty room name (invalid)
		}

		// Use Span to avoid substring allocations
		var inputSpan = input.AsSpan();
		var roomName = inputSpan.Slice(0, slashIndex).Trim().ToString();
		var objectsPart = inputSpan.Slice(slashIndex + 1).Trim().ToString();

		var objectNames = NameListString(objectsPart).ToList();

		return (roomName, objectNames);
	}

	/// <summary>
	/// A regular expression that matches one or more names in a list format.
	/// </summary>
	/// <returns>A regex that has a named group for the match.</returns>
	[GeneratedRegex("(\"(?<User>.+?)\"|(?<DBRef>#\\d+(:\\d+)?)|(?<User>\\S+))(\\s?|$)")]
	private static partial Regex NameListPattern();
}