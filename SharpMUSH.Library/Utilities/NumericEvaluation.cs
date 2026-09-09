using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Utilities;

/// <summary>Numeric argument conversion shared by dispatch validation and softcode evaluation.</summary>
public readonly record struct NumericEvaluation(bool TinyMath, bool NullEqualsZero)
{
	private const NumberStyles Integer = NumberStyles.AllowLeadingSign;
	private const NumberStyles Real = Integer | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

	public static NumericEvaluation For(IMUSHCodeParser parser)
	{
		var options = parser.ServiceProvider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Compatibility;
		return new(options.TinyMath, options.NullEqualsZero);
	}

	public bool TryInt64(string? input, out long value) => long.TryParse(Candidate(input, integer: true), Integer, CultureInfo.InvariantCulture, out value);
	public bool TryInt32(string? input, out int value) => int.TryParse(Candidate(input, integer: true), Integer, CultureInfo.InvariantCulture, out value);
	public bool TryUInt64(string? input, out ulong value)
	{
		var candidate = Candidate(input, integer: true);
		if (TinyMath && !candidate.IsEmpty && candidate[0] == '-')
		{
			var success = ulong.TryParse(candidate[1..], Integer, CultureInfo.InvariantCulture, out value);
			value = unchecked(0UL - value);
			return success;
		}
		return ulong.TryParse(candidate, Integer, CultureInfo.InvariantCulture, out value);
	}
	public bool TryDecimal(string? input, out decimal value)
	{
		var text = input.AsSpan().TrimStart(" \t\r\n\v\f");
		if (HexadecimalNumber.HasPrefix(text) && HexadecimalNumber.TryParse(text, TinyMath, out var number))
		{
			return HexadecimalNumber.TryConvertDecimal(number, out value);
		}
		return decimal.TryParse(Candidate(input, integer: false), Real, CultureInfo.InvariantCulture, out value);
	}
	public bool TryDouble(string? input, out double value)
	{
		var text = input.AsSpan().TrimStart(" \t\r\n\v\f");
		if (HexadecimalNumber.HasPrefix(text) && HexadecimalNumber.TryParse(text, TinyMath, out value)) return true;
		return double.TryParse(Candidate(input, integer: false), Real, CultureInfo.InvariantCulture, out value);
	}

	private ReadOnlySpan<char> Candidate(string? input, bool integer)
	{
		var text = input.AsSpan().TrimStart(" \t\r\n\v\f");
		if (text.IsEmpty) return TinyMath || NullEqualsZero ? "0" : text;
		if (!TinyMath) return text;
		var position = text[0] is '+' or '-' ? 1 : 0;
		var digits = 0;
		while (position < text.Length && char.IsAsciiDigit(text[position])) { position++; digits++; }
		if (!integer && position < text.Length && text[position] == '.')
		{
			position++;
			while (position < text.Length && char.IsAsciiDigit(text[position])) { position++; digits++; }
		}
		if (digits == 0) return "0";
		if (!integer && position < text.Length && text[position] is 'e' or 'E')
		{
			var exponent = position + 1;
			if (exponent < text.Length && text[exponent] is '+' or '-') exponent++;
			var firstDigit = exponent;
			while (exponent < text.Length && char.IsAsciiDigit(text[exponent])) exponent++;
			if (exponent > firstDigit) position = exponent;
		}
		return text[..position];
	}
}
