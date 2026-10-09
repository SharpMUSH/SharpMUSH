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
	/// <summary>
	/// An integer argument as PennMUSH reads one: <c>is_integer</c>, then <c>parse_integer</c>
	/// (<c>src/parse.c:373</c>). Leading whitespace is skipped and anything after the digits refuses it;
	/// an empty argument is 0 under NULL_EQ_ZERO, and under TINY_MATH every argument is an integer
	/// (its leading digits, or 0).
	/// </summary>
	public static bool TryInteger(IMUSHCodeParser parser, string? text, out int value)
		=> NumericEvaluation.For(parser).TryInt32(text, out value);

	/// <summary>
	/// PennMUSH's <c>parse_integer</c> (<c>hdrs/parse.h:54</c>, <c>parse_int</c> at <c>src/parse.c:674</c>):
	/// <c>strtol</c> with no format check, so the leading digits are the value, text with none is 0, and
	/// a value past an <c>int</c> is clamped to it. For the arguments PennMUSH reads without asking
	/// <c>is_integer</c> first, which therefore never fail.
	/// </summary>
	public static int ParseInteger(string? text)
	{
		if (new NumericEvaluation(TinyMath: true, NullEqualsZero: true).TryInt64(text, out long value))
		{
			return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
		}

		// More digits than a long holds: strtol saturates in the direction of the sign.
		return text.AsSpan().TrimStart(" \t\r\n\v\f").StartsWith("-") ? int.MinValue : int.MaxValue;
	}

	/// <summary>
	/// PennMUSH's <c>int_check</c> (<c>src/function.c:280-298</c>) for an optional integer argument:
	/// absent is <paramref name="defaultValue"/>, empty is 0 under NULL_EQ_ZERO and
	/// <paramref name="defaultValue"/> otherwise, and anything else must be a strict integer.
	/// </summary>
	/// <param name="text">The argument, or <see langword="null"/> when it was not given.</param>
	public static bool TryIntCheck(IMUSHCodeParser parser, string? text, int defaultValue, out int value)
	{
		if (text is null)
		{
			value = defaultValue;
			return true;
		}

		if (text.Length == 0)
		{
			value = NumericEvaluation.For(parser).NullEqualsZero ? 0 : defaultValue;
			return true;
		}

		return TryStrictInteger(text, out value);
	}

	/// <summary>
	/// PennMUSH's <c>is_number</c> then <c>parse_number</c> (<c>src/parse.c</c>): a real argument under
	/// the live TINY_MATH and NULL_EQ_ZERO options.
	/// </summary>
	public static bool TryNumber(IMUSHCodeParser parser, string? text, out double value)
		=> NumericEvaluation.For(parser).TryDouble(text, out value);

	/// <summary>
	/// PennMUSH's <c>is_uinteger</c> (<c>src/parse.c:429</c>): <see cref="TryInteger"/>, but the first
	/// character after the whitespace must be a digit or <c>+</c>. A negative value is refused under
	/// TINY_MATH too, where PennMUSH would wrap it to a huge unsigned one.
	/// </summary>
	public static bool TryUnsignedInteger(IMUSHCodeParser parser, string? text, out int value)
	{
		var numbers = NumericEvaluation.For(parser);
		if (!numbers.TinyMath && !StartsUnsigned(text))
		{
			value = 0;
			return false;
		}

		return numbers.TryInt32(text, out value) && value >= 0;
	}

	/// <summary>
	/// PennMUSH's <c>is_strict_integer</c> (<c>src/parse.c:556</c>): an integer whatever TINY_MATH and
	/// NULL_EQ_ZERO say, so an empty argument is refused.
	/// </summary>
	public static bool TryStrictInteger(string? text, out int value)
		=> NumericEvaluation.Strict.TryInt32(text, out value);

	/// <inheritdoc cref="TryStrictInteger"/>
	public static bool TryStrictInteger(string? text, out long value)
		=> NumericEvaluation.Strict.TryInt64(text, out value);

	/// <summary>PennMUSH's <c>is_strict_uinteger</c> (<c>src/parse.c:485</c>).</summary>
	public static bool TryStrictUnsignedInteger(string? text, out int value)
	{
		if (!StartsUnsigned(text))
		{
			value = 0;
			return false;
		}

		return NumericEvaluation.Strict.TryInt32(text, out value);
	}

	/// <summary>
	/// <see cref="TryStrictUnsignedInteger"/> over PennMUSH's whole <c>unsigned int</c> range, for a
	/// value such as a queue PID that can pass <see cref="int.MaxValue"/>.
	/// </summary>
	public static bool TryStrictUnsignedLong(string? text, out long value)
	{
		if (!StartsUnsigned(text))
		{
			value = 0;
			return false;
		}

		return NumericEvaluation.Strict.TryInt64(text, out value) && value <= uint.MaxValue;
	}

	/// <summary><c>isdigit(*str) || *str == '+'</c> after the leading whitespace.</summary>
	private static bool StartsUnsigned(string? text)
		=> text.AsSpan().TrimStart(" \t\r\n\v\f") is [] or [('+' or (>= '0' and <= '9')), ..];

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

		return ValueTask.FromResult<CallState>(result ?? 0);
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
		return ValueTask.FromResult<CallState>(result);
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
	/// The numbered arguments from <paramref name="first"/> on, each moved down one place (argument
	/// <c>i</c> becomes register <c>i - 1</c>): how a command or function hands its trailing arguments
	/// on as <c>%0</c>-<c>%9</c>. With <paramref name="requireMessage"/>, an argument with no message is
	/// left out.
	/// </summary>
	public static Dictionary<string, CallState> ShiftedArguments(IReadOnlyDictionary<string, CallState> args,
		int first, bool requireMessage = false)
		=> Enumerable.Range(first, Math.Max(0, args.Count - first))
			.Select(index => (index, found: args.TryGetValue(index.ToString(), out var value), value))
			.Where(arg => arg.found && (!requireMessage || arg.value!.Message != null))
			.ToDictionary(arg => (arg.index - 1).ToString(), arg => arg.value!);

	public static IEnumerable<DbRefOrName> NameList(string list)
		=> NameListPattern().Matches(list).Select(x =>
			HelperFunctions.ParseDbRef(x.Groups["DBRef"].Value) is DBRef dbref
				? new DbRefOrName(dbref)
				: new DbRefOrName(x.Groups["User"].Value));

	public static IEnumerable<string> NameListString(string list)
		=> NameListPattern().Matches(list).Select(x =>
			HelperFunctions.ParseDbRef(x.Groups["DBRef"].Value) is DBRef dbref
				? dbref.ToString()
				: x.Groups["User"].Value);

	/// <summary>
	/// PennMUSH's <c>lookup_desc</c> (<c>src/bsd.c:6629-6666</c>): how every per-connection function
	/// turns its argument into a descriptor, and the first of the two permission checks they make.
	/// <list type="bullet">
	/// <item>A descriptor number answers only for a Priv_Who caller, or for the caller's own
	/// descriptor. Anyone else learns nothing, not even whether the number is in use.</item>
	/// <item>A name is matched as a player (<c>me</c>, <c>#dbref</c> and <c>*name</c> included) and
	/// answers with that player's least idle connection. A connection hidden with <c>@hide</c> counts
	/// only for a Priv_Who caller, so a hidden player is simply not connected to everyone else.</item>
	/// </list>
	/// Null is the one failure: every caller turns it into its own "not connected" answer.
	/// The second check, on who may read a descriptor's private fields, is
	/// <see cref="CanReadDescriptorAsync"/>.
	/// </summary>
	public static async ValueTask<IConnectionService.ConnectionData?> LookupDescriptorAsync(
		IMUSHCodeParser parser, ILocateService locateService, IConnectionService connectionService,
		AnySharpObject executor, string name)
	{
		var privWho = await executor.IsSee_All();

		if (long.TryParse(name, out var handle))
		{
			var descriptor = connectionService.Get(handle);
			return descriptor is not null && (privWho || descriptor.Ref == executor.Object().DBRef)
				? descriptor
				: null;
		}

		var located = await locateService.LocateConnectionTarget(parser, executor, executor, name);
		if (located is not (AnySharpObject and SharpPlayer player))
		{
			return null;
		}

		return await VisibleConnectionAsync(connectionService, executor, player.Object.DBRef);
	}

	/// <summary>
	/// The descriptor <see cref="LookupDescriptorAsync"/> settles on for a player it has matched: the
	/// least idle one, counting a connection hidden with <c>@hide</c> only when
	/// <paramref name="executor"/> is Priv_Who.
	/// </summary>
	public static async ValueTask<IConnectionService.ConnectionData?> VisibleConnectionAsync(
		IConnectionService connectionService, AnySharpObject executor, DBRef player)
	{
		var privWho = await executor.IsSee_All();
		return await connectionService.Get(player)
			.Where(connection => privWho || !connection.IsHidden)
			.MinByAsync(connection => connection.Idle ?? TimeSpan.MaxValue);
	}

	/// <summary>
	/// The second check, which PennMUSH's <c>fun_hostname</c>, <c>fun_ipaddr</c>, <c>fun_cmds</c>,
	/// <c>fun_sent</c>, <c>fun_recv</c> and <c>fun_ssl</c> (<c>src/bsd.c</c>) make on the descriptor
	/// <see cref="LookupDescriptorAsync"/> found: its address and traffic belong to the player on it
	/// (<paramref name="player"/>) and to See_All. The other per-connection functions answer anyone who
	/// got past the lookup.
	/// </summary>
	public static async ValueTask<bool> CanReadDescriptorAsync(AnySharpObject executor, DBRef? player)
		=> player == executor.Object().DBRef || await executor.IsSee_All();

	/// <summary>
	/// The descriptor PennMUSH's <c>lookup_desc()</c> (<c>src/bsd.c</c>) settles on for a player: it
	/// walks the whole connected list and keeps the one with the greatest <c>last_time</c> — the
	/// least idle. A player with two clients open therefore answers <c>idle()</c>, <c>terminfo()</c>,
	/// <c>width()</c> and the rest about the one they are actually using, which taking whichever
	/// connection came out of the dictionary first did only by luck.
	/// </summary>
	public static ValueTask<IConnectionService.ConnectionData?> LeastIdleConnectionAsync(
		IConnectionService connectionService, DBRef who)
		=> connectionService.Get(who).MinByAsync(connection => connection.Idle ?? TimeSpan.MaxValue);

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

		if (await mediator.Send(new GetObjectNodeQuery(reference)) is not AnySharpObject found)
		{
			return null;
		}

		var names = await found.Object().Flags.Value
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
	[GeneratedRegex("(\"(?<User>.+?)\"|(?<DBRef>#\\d+(:\\d+)?)(?=\\s|$)|(?<User>\\S+))(\\s?|$)")]
	private static partial Regex NameListPattern();
}