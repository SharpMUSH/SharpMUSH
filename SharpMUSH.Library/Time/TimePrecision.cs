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
			case "seconds":
				precision = TimePrecision.Seconds;
				return true;
			case "f":
			case "fractional":
				precision = TimePrecision.Fractional;
				return true;
			case "ms":
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
	/// Reads a seconds value as a whole-second part and a millisecond remainder. <b>Both parts carry
	/// the sign</b>, so a caller rejecting negative durations must test both.
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

		// Split the magnitude and reapply the sign, rather than reading the sign back off the
		// truncated whole part. decimal.Truncate returns 0 for everything in (-1, 0), so a value like
		// -0.5 would otherwise arrive as a positive 500ms with nothing left to say it was negative —
		// and timestring(-0.5) would render "0s" instead of refusing a negative duration.
		var negative = parsed < 0;
		var magnitude = Math.Abs(parsed);
		var whole = decimal.Truncate(magnitude);
		var fraction = decimal.Round((magnitude - whole) * 1000m, MidpointRounding.AwayFromZero);

		// Rounding .9995 up lands on a full second, which belongs in the whole part.
		if (fraction >= 1000m)
		{
			whole += 1;
			fraction -= 1000m;
		}

		try
		{
			seconds = negative ? -(long)whole : (long)whole;
		}
		catch (OverflowException)
		{
			return false;
		}

		milliseconds = negative ? -(int)fraction : (int)fraction;
		return true;
	}

	/// <summary>
	/// Reads a seconds value naming an instant, rejecting anything outside the range
	/// <see cref="DateTimeOffset"/> can represent.
	/// </summary>
	/// <remarks>
	/// The range check is the point. Callers used to hand the parsed value straight to
	/// <see cref="DateTimeOffset.FromUnixTimeMilliseconds"/>, which throws for a value a caller can
	/// trivially supply — a millisecond stamp passed where seconds are expected is 1000x too large
	/// and lands well outside it. The exception escaped into the function dispatcher and came back as
	/// an empty result rather than an error, which is the one answer softcode cannot tell from a real
	/// one.
	/// </remarks>
	public static bool TryParseInstant(string? value, out DateTimeOffset instant)
	{
		instant = default;

		if (!TryParseSeconds(value, out var milliseconds)
				|| milliseconds < MinInstantMilliseconds
				|| milliseconds > MaxInstantMilliseconds)
		{
			return false;
		}

		instant = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
		return true;
	}

	private static readonly long MinInstantMilliseconds = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();

	private static readonly long MaxInstantMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

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
