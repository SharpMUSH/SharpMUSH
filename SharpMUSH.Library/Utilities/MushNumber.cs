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

	/// <summary>
	/// A decimal exactly halfway between two values at the precision is rounded the way <c>%.*f</c> rounds the
	/// double nearest it, which lies a little above or below the halfway point: <c>fdiv(3.141593,2)</c> is
	/// 1.5707965, written 1.570796.
	/// </summary>
	public static string Unparse(decimal value)
		=> Trim(IsHalfway(value)
			? Unparse(double.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture))
			: value.ToString(Fixed(), CultureInfo.InvariantCulture));

	/// <summary>
	/// Whether <paramref name="value"/> ends in a 5 just past the precision. A value too large to scale has no
	/// digit there.
	/// </summary>
	private static bool IsHalfway(decimal value)
	{
		var place = Pow10(Configurable.FloatPrecision);
		var magnitude = Math.Abs(value);
		if (magnitude > decimal.MaxValue / place) return false;

		var scaled = magnitude * place;
		return scaled - decimal.Truncate(scaled) == 0.5m;
	}

	private static decimal Pow10(int exponent)
	{
		var result = 1m;
		for (var i = 0; i < exponent; i++) result *= 10m;
		return result;
	}

	private static string Fixed() => string.Create(CultureInfo.InvariantCulture, $"F{Configurable.FloatPrecision}");

	private static string Trim(string text)
	{
		if (text.Contains('.'))
			text = text.TrimEnd('0').TrimEnd('.');

		return text == "-0" ? "0" : text;
	}
}
