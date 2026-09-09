namespace SharpMUSH.Library.Utilities;

/// <summary>Linear, constant-space hexadecimal floating-point conversion with ties-to-even rounding.</summary>
internal static class HexadecimalNumber
{
	public static bool HasPrefix(ReadOnlySpan<char> text)
	{
		if (!text.IsEmpty && text[0] is '+' or '-') text = text[1..];
		return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
	}

	public static bool TryParse(ReadOnlySpan<char> text, bool allowTrailing, out double value)
	{
		value = 0;
		if (!HasPrefix(text)) return false;
		var negative = text[0] == '-';
		var position = text[0] is '+' or '-' ? 3 : 2;
		var digits = 0;
		var fractionalDigits = 0;
		var fractional = false;
		var totalBits = 0;
		var storedBits = 0;
		ulong prefix = 0;
		var sticky = false;
		while (position < text.Length)
		{
			var c = text[position];
			if (c == '.' && !fractional) { fractional = true; position++; continue; }
			var digit = c is >= '0' and <= '9' ? c - '0'
				: c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
			if (digit < 0) break;
			digits++;
			if (fractional) fractionalDigits++;
			for (var bit = 3; bit >= 0; bit--)
			{
				var set = (digit >> bit) & 1;
				if (totalBits == 0 && set == 0) continue;
				totalBits++;
				if (storedBits < 60) { prefix = (prefix << 1) | (uint)set; storedBits++; }
				else sticky |= set != 0;
			}
			position++;
		}
		if (digits == 0) return false;
		long exponent = 0;
		if (position < text.Length && text[position] is 'p' or 'P')
		{
			var exponentStart = position++;
			var exponentNegative = position < text.Length && text[position] == '-';
			if (position < text.Length && text[position] is '+' or '-') position++;
			var first = position;
			while (position < text.Length && char.IsAsciiDigit(text[position]))
			{
				exponent = Math.Min(1_000_000_000, exponent * 10 + text[position++] - '0');
			}
			if (first == position) position = exponentStart;
			else if (exponentNegative) exponent = -exponent;
		}
		if (!allowTrailing && position != text.Length) return false;
		exponent += totalBits - 1L - 4L * fractionalDigits;
		if (totalBits == 0 || exponent < -1075) value = 0;
		else if (exponent > 1023) value = double.PositiveInfinity;
		else if (exponent == -1075)
			value = prefix != (1UL << (storedBits - 1)) || sticky ? double.Epsilon : 0;
		else
		{
			// Subnormals have fewer significant bits. Round directly at their precision to avoid
			// double rounding near the halfway point between zero and the smallest subnormal.
			var precision = (int)Math.Min(53, exponent + 1075);
			if (storedBits > precision)
			{
				var shift = storedBits - precision;
				var remainder = prefix & ((1UL << shift) - 1);
				var halfway = 1UL << (shift - 1);
				prefix >>= shift;
				if (remainder > halfway || remainder == halfway && (sticky || (prefix & 1) != 0)) prefix++;
				storedBits = precision;
			}
			value = Math.ScaleB(prefix, (int)exponent - storedBits + 1);
		}
		if (negative) value = -value;
		return true;
	}
}
