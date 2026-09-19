using System.Globalization;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// Writes a floating-point result as softcode sees it: PennMUSH's <c>unparse_number</c>
/// (<c>src/unparse.c:251</c>), <c>%.*f</c> at <see cref="Configurable.FloatPrecision"/> decimal places
/// with trailing zeros and a bare decimal point dropped. Never exponent notation. A value that rounds
/// to zero from below is written <c>0</c>, where C's <c>printf</c> leaves <c>-0</c>.
/// </summary>
public static class MushNumber
{
	public static string Unparse(double value)
		=> double.IsFinite(value)
			? Trim(value.ToString(Fixed(), CultureInfo.InvariantCulture))
			: value.ToString(CultureInfo.InvariantCulture);

	public static string Unparse(decimal value)
		=> Trim(value.ToString(Fixed(), CultureInfo.InvariantCulture));

	private static string Fixed() => string.Create(CultureInfo.InvariantCulture, $"F{Configurable.FloatPrecision}");

	private static string Trim(string text)
	{
		if (text.Contains('.'))
			text = text.TrimEnd('0').TrimEnd('.');

		return text == "-0" ? "0" : text;
	}
}
