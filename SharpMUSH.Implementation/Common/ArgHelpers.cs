using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Common;

public static partial class ArgHelpers
{
	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		MString defaultValue)
	{
		if (args.Count - 1 < item || item == 0 && string.IsNullOrEmpty(args[item.ToString()]?.Message?.ToPlainText()) ||
				args[item.ToString()].Message?.ToPlainText() is null)
		{
			return defaultValue;
		}

		return args[item.ToString()].Message!;
	}

	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		string defaultValue)
		=> NoParseDefaultNoParseArgument(args, item, MarkupText.Plain(defaultValue));

	public static async ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		MString defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		if (args.Count - 1 < item || args[item.ToString()].Message!.Length == 0)
		{
			return defaultValue;
		}

		return (await args[item.ToString()].ParsedMessage())!;
	}

	public static ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		string defaultValue)
		=> NoParseDefaultEvaluatedArgument(parser, item, MarkupText.Plain(defaultValue));

	public static async ValueTask<MString> EvaluatedDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		CallState defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		var parsedValue = (await args[item.ToString()].ParsedMessage())!;
		if (args.Count - 1 < item || parsedValue.Length == 0)
		{
			return (await defaultValue.ParsedMessage())!;
		}

		return parsedValue;
	}

	public static ValueTask<CallState> AggregateDecimals(IMUSHCodeParser parser,
		Func<decimal, decimal, decimal> aggregateFunction)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var numbers = NumericEvaluation.For(parser);
		decimal? result = null;

		foreach (var arg in args)
		{
			var text = (arg.Value.Message ?? MarkupText.Empty).ToPlainText();
			if (!numbers.TryDecimal(text, out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}

			result = result is { } accumulated ? aggregateFunction(accumulated, value) : value;
		}

		return ValueTask.FromResult<CallState>(FormatDecimal(result ?? 0));
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
	/// rendered signed, because PennMUSH renders it that way too: safe_uinteger (src/strutil.c) hands
	/// the value to unparse_integer, which takes an intmax_t, so bnot(0) is -1, not 18446744073709551615.
	/// </summary>
	public static ValueTask<CallState> AggregateUnsignedIntegers(IMUSHCodeParser parser,
		Func<ulong, ulong, ulong> aggregateFunction)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var numbers = NumericEvaluation.For(parser);
		ulong? result = null;

		foreach (var arg in args)
		{
			var text = (arg.Value.Message ?? MarkupText.Empty).ToPlainText();
			if (!numbers.TryUInt64(text, out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.UIntegers);
			}

			result = result is { } accumulated ? aggregateFunction(accumulated, value) : value;
		}

		return ValueTask.FromResult<CallState>(long.CreateTruncating(result ?? 0).ToString(CultureInfo.InvariantCulture));
	}

	/// <inheritdoc cref="AggregateUnsignedIntegers"/>
	public static ValueTask<CallState> EvaluateUnsignedInteger(IMUSHCodeParser parser,
		Func<ulong, ulong> func)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var numbers = NumericEvaluation.For(parser);
		var text = (args["0"].Message ?? MarkupText.Empty).ToPlainText();
		if (!numbers.TryUInt64(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.UInteger);
		}

		return ValueTask.FromResult<CallState>(long.CreateTruncating(func(value)).ToString(CultureInfo.InvariantCulture));
	}

	public static ValueTask<CallState> EvaluateDecimal(IMUSHCodeParser parser,
		Func<decimal, decimal> func)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var numbers = NumericEvaluation.For(parser);
		var text = (args["0"].Message ?? MarkupText.Empty).ToPlainText();
		if (!numbers.TryDecimal(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		var result = func(value);
		return ValueTask.FromResult<CallState>(FormatDecimal(result));
	}

	public static ValueTask<CallState> EvaluateDecimalToInteger(IMUSHCodeParser parser,
		Func<decimal, long> func)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var numbers = NumericEvaluation.For(parser);
		var text = (args["0"].Message ?? MarkupText.Empty).ToPlainText();
		if (!numbers.TryDecimal(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		return ValueTask.FromResult<CallState>(func(value));
	}

	public static ValueTask<CallState> EvaluateDouble(IMUSHCodeParser parser,
		Func<double, double> func)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var numbers = NumericEvaluation.For(parser);
		var text = (args["0"].Message ?? MarkupText.Empty).ToPlainText();
		if (!numbers.TryDouble(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		return ValueTask.FromResult<CallState>(func(value));
	}


	public static ValueTask<CallState> ValidateDecimalAndEvaluatePairwise(
		IMUSHCodeParser parser,
		Func<(decimal, decimal), bool> func, bool negate = false)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var numbers = NumericEvaluation.For(parser);
		if (args.Count < 2)
		{
			return ValueTask.FromResult(new CallState(Message: ErrorMessages.Returns.TooFewArguments));
		}

		// Every argument is checked before any comparison decides the answer: a non-number anywhere in
		// the list is the error, even after a pair that already failed the comparison.
		var result = true;
		decimal? previous = null;
		foreach (var arg in args)
		{
			if (!numbers.TryDecimal((arg.Value.Message ?? MarkupText.Empty).ToPlainText(), out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}

			if (previous is { } left)
			{
				result &= func((left, value));
			}

			previous = value;
		}

		return new ValueTask<CallState>(result != negate ? "1" : "0");
	}

	/// <summary>
	/// Flag and power names are matched case-insensitively, by name or alias: PennMUSH's
	/// <c>match_flag</c> / <c>match_power</c> (<c>src/flags.c:124-149</c>) both resolve through
	/// <c>ptab_find</c>, which compares with <c>strcasecmp</c> and <c>string_prefix</c>, and
	/// <c>string_prefix</c> compares through <c>DOWNCASE</c>.
	/// <para>
	/// Both of these forward to <see cref="HelperFunctions"/> so there is exactly one answer to
	/// "does this object have this flag/power?" — the two used to be separate implementations that
	/// disagreed, which made a permission check depend on which helper the call site reached for.
	/// </para>
	/// </summary>
	public static ValueTask<bool> HasObjectFlags(SharpObject obj, SharpObjectFlag flag)
		=> obj.HasFlag(flag.Name);

	/// <inheritdoc cref="HasObjectFlags"/>
	public static ValueTask<bool> HasObjectPowers(SharpObject obj, string power)
		=> obj.HasPower(power);

	public static IEnumerable<DbRefOrName> NameList(string list)
		=> NameListPattern().Matches(list).Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? new DbRefOrName(HelperFunctions.ParseDbRef(x.Groups["DBRef"].Value).AsValue())
				: new DbRefOrName(x.Groups["User"].Value));

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
		var valueText = (value.Message ?? MarkupText.Empty).ToPlainText();

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
	/// The colour flags set on whoever is behind a descriptor, or null at the connect screen where
	/// there is nobody behind it yet. They belong in every answer about a connection's colour depth:
	/// a flag can raise the depth above what the terminal negotiated, so a report built from terminal
	/// metadata alone describes a connection that is not the one being rendered — a "dumb" terminal on
	/// a player with XTERM256 receives 256-colour output while terminfo() calls it "hilite".
	/// </summary>
	public static async ValueTask<PlayerColorFlags?> ColorFlagsOfAsync(IMediator mediator, DBRef? who)
	{
		if (who is not { } reference)
		{
			return null;
		}

		var found = await mediator.Send(new GetObjectNodeQuery(reference));
		if (found.IsNone)
		{
			return null;
		}

		var names = await found.Known.Object().Flags.Value
			.Select(flag => flag.Name)
			.ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

		return new PlayerColorFlags(
			Ansi: names.Contains("ANSI"),
			Color: names.Contains("COLOR"),
			Xterm256: names.Contains("XTERM256"),
			Truecolor: names.Contains("TRUECOLOR"));
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