using System.Globalization;
using System.Text;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// TinyMUX's ways of writing a number of seconds: <c>exptime()</c>, <c>writetime()</c>, <c>singletime()</c> and
/// <c>digittime()</c>, and <c>restartsecs()</c>. Each reads the number at the start of its argument, as TinyMUX does,
/// and counts anything below zero as zero.
/// </summary>
public partial class Functions
{
	/// <summary>The units these functions write, largest first: a year and a month are their average lengths.</summary>
	private static readonly (long Seconds, char Letter, string Name)[] ElapsedUnits =
	[
		(31_556_926, 'y', "year"),
		(2_629_743, 'M', "month"),
		(604_800, 'w', "week"),
		(86_400, 'd', "day"),
		(3_600, 'h', "hour"),
		(60, 'm', "minute"),
		(1, 's', "second")
	];

	/// <summary><c>exptime(&lt;seconds&gt;)</c>: every unit that is not zero, by letter (<c>3h 25m 45s</c>).</summary>
	[SharpFunction(Name = "exptime", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["seconds"])]
	public ValueTask<CallState> ExpTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(WriteElapsed(ElapsedArgument(parser), single: false, names: false));

	/// <summary><c>writetime(&lt;seconds&gt;)</c>: every unit that is not zero, by name (<c>3 hours 25 minutes 45 seconds</c>).</summary>
	[SharpFunction(Name = "writetime", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["seconds"])]
	public ValueTask<CallState> WriteTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(WriteElapsed(ElapsedArgument(parser), single: false, names: true));

	/// <summary><c>singletime(&lt;seconds&gt;)</c>: the largest unit alone, rounded down (<c>3h</c>).</summary>
	[SharpFunction(Name = "singletime", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["seconds"])]
	public ValueTask<CallState> SingleTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(WriteElapsed(ElapsedArgument(parser), single: true, names: false));

	/// <summary><c>digittime(&lt;seconds&gt;)</c>: hours and minutes as <c>HH:MM</c>, after the days when there are any.</summary>
	[SharpFunction(Name = "digittime", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["seconds"])]
	public ValueTask<CallState> DigitTime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var seconds = ElapsedArgument(parser);
		var days = seconds / 86_400;
		var clock = string.Create(CultureInfo.InvariantCulture, $"{seconds % 86_400 / 3_600:00}:{seconds % 3_600 / 60:00}");
		return ValueTask.FromResult<CallState>(days > 0
			? string.Create(CultureInfo.InvariantCulture, $"{days}d {clock}")
			: clock);
	}

	/// <summary><c>restartsecs()</c>: when the game last restarted, in seconds since the epoch.</summary>
	[SharpFunction(Name = "restartsecs", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> RestartSecs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var data = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();
		return (data?.LastRebootTime ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>The number of seconds the first argument starts with, and zero for a negative one.</summary>
	private static long ElapsedArgument(IMUSHCodeParser parser)
		=> Math.Max(0, LeadingInteger(parser.CurrentState.Arguments["0"].Message.ToPlainText()));

	/// <summary>
	/// <paramref name="seconds"/> as units: each that is not zero, or with <paramref name="single"/> only the
	/// largest; by letter, or with <paramref name="names"/> by name, plural unless one.
	/// </summary>
	private static string WriteElapsed(long seconds, bool single, bool names)
	{
		var text = new StringBuilder();
		var remaining = seconds;
		foreach (var (unitSeconds, letter, name) in ElapsedUnits)
		{
			var count = remaining / unitSeconds;
			remaining -= count * unitSeconds;
			// The seconds are always written, even as 0 after larger units (1h 0s), as TinyMUX writes them.
			if (count == 0 && unitSeconds != 1) continue;

			if (text.Length > 0) text.Append(' ');
			text.Append(count.ToString(CultureInfo.InvariantCulture));
			if (names) text.Append(' ').Append(name).Append(count == 1 ? "" : "s");
			else text.Append(letter);

			if (single) break;
		}

		return text.ToString();
	}
}
