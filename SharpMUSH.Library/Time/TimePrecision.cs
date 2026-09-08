using System.Globalization;

namespace SharpMUSH.Library.Time;

/// <summary>
/// The unit a time function renders its numeric result in.
/// </summary>
/// <remarks>
/// Precision describes <em>output</em> only. Every number a time function reads is seconds,
/// possibly fractional — nothing inspects a value's magnitude to guess its unit, so a call
/// that is textually identical in PennMUSH and SharpMUSH computes the same thing.
/// </remarks>
public enum TimePrecision
{
	/// <summary>Whole seconds — PennMUSH's unit, and what an omitted precision argument means.</summary>
	Seconds,

	/// <summary>Seconds carrying a fractional part, trailing zeros trimmed.</summary>
	Fractional,

	/// <summary>Whole milliseconds, which is what SharpMUSH stores and what an objid carries.</summary>
	Milliseconds
}

/// <summary>
/// The one place the precision-argument contract lives: which tokens name which unit, how a
/// stored millisecond value is rendered in each, and how an incoming seconds value is read.
/// </summary>
public static class TimePrecisions
{
	/// <summary>
	/// Reads a precision argument. An omitted, empty or whitespace token is
	/// <see cref="TimePrecision.Seconds"/>, so every PennMUSH call site keeps its unit.
	/// </summary>
	/// <remarks>
	/// An unrecognised token fails rather than falling back to seconds. A silent fallback would
	/// turn a typo into a plausible-looking wrong number, which is the failure the precision
	/// argument exists to prevent.
	/// </remarks>
	public static bool TryParse(string? token, out TimePrecision precision)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			precision = TimePrecision.Seconds;
			return true;
		}

		switch (token.Trim().ToLowerInvariant())
		{
			case "s":
			case "sec":
			case "secs":
			case "second":
			case "seconds":
				precision = TimePrecision.Seconds;
				return true;
			case "f":
			case "fractional":
				precision = TimePrecision.Fractional;
				return true;
			case "ms":
			case "milli":
			case "millis":
			case "millisecond":
			case "milliseconds":
				precision = TimePrecision.Milliseconds;
				return true;
			default:
				precision = TimePrecision.Seconds;
				return false;
		}
	}

	/// <summary>
	/// Renders a stored millisecond value in <paramref name="precision"/>.
	/// </summary>
	public static string Format(long milliseconds, TimePrecision precision)
		=> precision switch
		{
			TimePrecision.Milliseconds => milliseconds.ToString(CultureInfo.InvariantCulture),
			TimePrecision.Fractional => FormatFractional(milliseconds),
			_ => ToWholeSeconds(milliseconds).ToString(CultureInfo.InvariantCulture)
		};

	/// <summary>
	/// Milliseconds to whole seconds, rounding towards negative infinity.
	/// </summary>
	/// <remarks>
	/// Floor rather than truncation, because that is what <see cref="DateTimeOffset.ToUnixTimeSeconds"/>
	/// does for pre-epoch instants: -1500ms is -2s there, and a second convention here would make
	/// <c>csecs()</c> and <c>convsecs()</c> disagree about the same moment. This is about naming an
	/// instant; <see cref="TryParseSecondsParts"/> splits a signed <em>duration</em> and truncates
	/// towards zero instead, since its two parts are read back as one signed magnitude.
	/// </remarks>
	public static long ToWholeSeconds(long milliseconds)
		=> (long)Math.Floor(milliseconds / 1000m);

	/// <summary>
	/// Reads a seconds value, which may carry a fractional part, as milliseconds.
	/// </summary>
	/// <remarks>
	/// Invariant culture is load-bearing, not hygiene: under a comma decimal separator a
	/// current-culture parse reads "1.5" as 15, so a game's arithmetic would depend on its host's
	/// locale. Exponent notation is rejected — PennMUSH does not accept it, and accepting it here
	/// would make "1e3" mean something SharpMUSH-only in an otherwise portable expression.
	/// </remarks>
	public static bool TryParseSeconds(string? value, out long milliseconds)
	{
		milliseconds = 0;

		if (!TryParseDecimalSeconds(value, out var seconds))
		{
			return false;
		}

		try
		{
			milliseconds = (long)decimal.Round(seconds * 1000m, MidpointRounding.AwayFromZero);
			return true;
		}
		catch (OverflowException)
		{
			return false;
		}
	}

	/// <summary>
	/// Reads a seconds value as a whole-second part and a millisecond remainder.
	/// </summary>
	/// <remarks>
	/// The duration renderers use this rather than <see cref="TryParseSeconds"/> because a single
	/// millisecond <see cref="long"/> only reaches ~292 million years, while a seconds one reaches
	/// ~292 billion. timestring() documents and tests the full 64-bit seconds range, so collapsing
	/// the two parts into one number would narrow an input range by a factor of 1000 to buy a
	/// precision nobody asks for at that magnitude.
	/// </remarks>
	public static bool TryParseSecondsParts(string? value, out long seconds, out int milliseconds)
	{
		seconds = 0;
		milliseconds = 0;

		if (!TryParseDecimalSeconds(value, out var parsed))
		{
			return false;
		}

		// Truncation towards zero, not floor: the two parts are read back as one signed magnitude
		// ("-1.500"), so -1.5 has to split as -1 and 500, never -2 and 500.
		var whole = decimal.Truncate(parsed);
		var fraction = decimal.Round(Math.Abs(parsed - whole) * 1000m, MidpointRounding.AwayFromZero);

		// Rounding .9995 up lands on a full second, which belongs in the whole part.
		if (fraction >= 1000m)
		{
			whole += whole < 0 ? -1 : 1;
			fraction -= 1000m;
		}

		try
		{
			seconds = (long)whole;
		}
		catch (OverflowException)
		{
			return false;
		}

		milliseconds = (int)fraction;
		return true;
	}

	/// <summary>
	/// The seconds field of a rendered duration, at <paramref name="precision"/>: whole as
	/// PennMUSH writes it, a trimmed fraction, or exactly three decimal places.
	/// </summary>
	public static string FormatSecondsField(long seconds, int milliseconds, TimePrecision precision)
		=> precision switch
		{
			TimePrecision.Milliseconds =>
				$"{seconds.ToString(CultureInfo.InvariantCulture)}.{milliseconds:D3}",
			TimePrecision.Fractional => milliseconds == 0
				? seconds.ToString(CultureInfo.InvariantCulture)
				: $"{seconds.ToString(CultureInfo.InvariantCulture)}.{milliseconds:D3}".TrimEnd('0'),
			_ => seconds.ToString(CultureInfo.InvariantCulture)
		};

	private static bool TryParseDecimalSeconds(string? value, out decimal seconds)
	{
		seconds = 0;

		return !string.IsNullOrWhiteSpace(value)
			&& decimal.TryParse(value.Trim(),
				NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
				CultureInfo.InvariantCulture, out seconds);
	}

	// Same invariant "trim the trailing zeros" shape as ArgHelpers.FormatDecimal, so a fractional
	// second reads like every other non-integer this server prints.
	private static string FormatFractional(long milliseconds)
		=> (milliseconds / 1000m).ToString("0.##########", CultureInfo.InvariantCulture);
}
