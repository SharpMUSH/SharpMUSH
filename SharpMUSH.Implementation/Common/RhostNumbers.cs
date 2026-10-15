using System.Globalization;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// How the functions brought over from RhostMUSH read a number out of a word: the number at its
/// start, if any, and zero otherwise. They never refuse a word for not being a number, so
/// <c>ladd(1 x 2)</c> is 3 and an <c>inf()</c> start of <c>:a:&gt;:</c> counts from 0.
/// </summary>
public static class RhostNumbers
{
	/// <summary>
	/// The whole number <paramref name="text"/> starts with, after any leading white space: an optional
	/// sign and the digits after it. Anything else, including a fraction, ends it; no digits is 0. A
	/// number too large for an int is held at its bound.
	/// </summary>
	public static int LeadingInteger(ReadOnlySpan<char> text)
	{
		var position = SkipWhiteSpace(text);
		var negative = false;
		if (position < text.Length && text[position] is '+' or '-')
		{
			negative = text[position] == '-';
			position++;
		}

		long value = 0;
		for (; position < text.Length && char.IsAsciiDigit(text[position]); position++)
		{
			value = Math.Min(value * 10 + (text[position] - '0'), (long)int.MaxValue + 1);
		}

		value = negative ? -value : value;
		return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
	}

	/// <summary>
	/// The number <paramref name="text"/> starts with, after any leading white space: a sign, digits
	/// with an optional fraction, and an optional exponent. No number at the start is 0.
	/// </summary>
	public static decimal LeadingNumber(ReadOnlySpan<char> text)
	{
		var start = SkipWhiteSpace(text);
		var position = start;
		if (position < text.Length && text[position] is '+' or '-') position++;

		var digits = 0;
		for (; position < text.Length && char.IsAsciiDigit(text[position]); position++) digits++;
		if (position < text.Length && text[position] == '.')
		{
			position++;
			for (; position < text.Length && char.IsAsciiDigit(text[position]); position++) digits++;
		}

		if (digits == 0) return 0m;

		var end = position;
		if (position < text.Length && text[position] is 'e' or 'E')
		{
			var exponent = position + 1;
			if (exponent < text.Length && text[exponent] is '+' or '-') exponent++;
			var exponentDigits = exponent;
			while (exponentDigits < text.Length && char.IsAsciiDigit(text[exponentDigits])) exponentDigits++;
			if (exponentDigits > exponent) end = exponentDigits;
		}

		return decimal.TryParse(text[start..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0m;
	}

	private static int SkipWhiteSpace(ReadOnlySpan<char> text)
	{
		var position = 0;
		while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
		return position;
	}
}
