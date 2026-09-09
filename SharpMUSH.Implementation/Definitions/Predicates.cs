using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Definitions;

public static class Predicates
{
	public static bool Truthy(MString text, bool tinyBooleans = false)
	{
		var plain = text.ToPlainText();
		if (tinyBooleans)
		{
			// Match the oracle's signed 64-bit prefix conversion followed by a 32-bit boolean value.
			var prefix = plain.AsSpan().TrimStart(" \t\r\n\v\f");
			var negative = !prefix.IsEmpty && prefix[0] == '-';
			if (!prefix.IsEmpty && prefix[0] is '+' or '-') prefix = prefix[1..];
			var limit = negative ? 1UL << 63 : long.MaxValue;
			ulong value = 0;
			foreach (var character in prefix)
			{
				if (character is < '0' or > '9') break;
				var digit = (uint)(character - '0');
				value = value > (limit - digit) / 10 ? limit : value * 10 + digit;
			}
			return unchecked((int)value) != 0;
		}

		if (plain.Length == 0 || plain.StartsWith("#-", StringComparison.Ordinal)) return false;
		if (IsHexadecimalZero(plain)) return false;
		// Penn accepts leading numeric whitespace, but a trailing blank makes the value text.
		const NumberStyles numeric = NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign
			| NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
		if (double.TryParse(plain, numeric, CultureInfo.InvariantCulture, out var number) && number == 0)
		{
			// An underflowing nonzero significand is text in Penn, not numeric zero.
			var significand = plain.AsSpan();
			var exponent = significand.IndexOfAny('e', 'E');
			if (exponent >= 0) significand = significand[..exponent];
			return significand.ContainsAnyInRange('1', '9');
		}
		return plain.AsSpan().Trim(' ').Length > 0;
	}

	private static bool IsHexadecimalZero(ReadOnlySpan<char> text)
	{
		text = text.TrimStart(" \t\r\n\v\f");
		if (!text.IsEmpty && text[0] is '+' or '-') text = text[1..];
		if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
		text = text[2..];
		var exponentIndex = text.IndexOfAny('p', 'P');
		if (exponentIndex >= 0)
		{
			var exponent = text[(exponentIndex + 1)..];
			if (!exponent.IsEmpty && exponent[0] is '+' or '-') exponent = exponent[1..];
			if (exponent.IsEmpty || exponent.ContainsAnyExceptInRange('0', '9')) return false;
			text = text[..exponentIndex];
		}
		var digitSeen = false;
		var pointSeen = false;
		foreach (var character in text)
		{
			if (character == '0') digitSeen = true;
			else if (character == '.' && !pointSeen) pointSeen = true;
			else return false;
		}
		return digitSeen;
	}

	public static bool Falsy(MString text, bool tinyBooleans = false) => !Truthy(text, tinyBooleans);
}

public static class PredicateExtensions
{
	public static bool Truthy(this MString? text) => text != null && Predicates.Truthy(text);
	public static bool Falsy(this MString? text) => !text.Truthy();

	public static bool Truthy(this MString? text, IMUSHCodeParser parser) => text != null && Predicates.Truthy(text,
		parser.ServiceProvider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Compatibility.TinyBooleans);
	public static bool Falsy(this MString? text, IMUSHCodeParser parser) => !text.Truthy(parser);
}
