using System.Globalization;
using System.Text;

namespace SharpMUSH.Configuration.Generated;

/// <summary>
/// Turns constants read out of the compilation into C# source text. Shared by the config generators so
/// the two do not drift: a type one of them handles and the other does not is a value silently dropped
/// from generated output.
/// </summary>
internal static class Emit
{
	/// <summary>
	/// A C# literal for a boxed constant, including its type. <c>GetDeclaredDefault</c> and
	/// <c>SharpConfigAttribute.Min</c>/<c>Max</c> are typed <c>object?</c>, so a uint written as a bare
	/// number would come back an int and compare unequal to the value it describes.
	/// </summary>
	/// <returns>
	/// <c>null</c> as source text for a type with no representation here. Every constant an options record
	/// or a SharpConfig attribute can currently hold is covered; the fallback exists so an exotic constant
	/// cannot emit uncompilable source.
	/// </returns>
	public static string Literal(object? value) => value switch
	{
		null => "null",
		bool b => b ? "true" : "false",
		string str => "\"" + Escape(str) + "\"",
		char c => CharLiteral(c),
		sbyte sb => "(sbyte)" + sb.ToString(CultureInfo.InvariantCulture),
		byte by => "(byte)" + by.ToString(CultureInfo.InvariantCulture),
		short sh => "(short)" + sh.ToString(CultureInfo.InvariantCulture),
		ushort us => "(ushort)" + us.ToString(CultureInfo.InvariantCulture),
		int i => i.ToString(CultureInfo.InvariantCulture),
		uint u => u.ToString(CultureInfo.InvariantCulture) + "u",
		long l => l.ToString(CultureInfo.InvariantCulture) + "L",
		ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
		double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
		float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
		decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
		_ => "null"
	};

	/// <summary>A double-quoted C# string literal, or null for a null input so callers can pick a fallback.</summary>
	public static string? Quote(string? value)
		=> value is null ? null : "\"" + Escape(value) + "\"";

	/// <summary>A single-quoted C# character literal.</summary>
	public static string CharLiteral(char value)
		=> value == '\'' ? "'\\''" : "'" + EscapeChar(value, quote: '\'') + "'";

	/// <summary>
	/// Escapes a string for a double-quoted C# literal. Control characters matter as much as quotes and
	/// backslashes: a description carrying a newline would otherwise split the literal across lines and
	/// the generated file would not compile.
	/// </summary>
	public static string Escape(string value)
	{
		var builder = new StringBuilder(value.Length);

		foreach (var c in value)
		{
			builder.Append(EscapeChar(c, quote: '"'));
		}

		return builder.ToString();
	}

	private static string EscapeChar(char c, char quote) => c switch
	{
		'\\' => "\\\\",
		'\0' => "\\0",
		'\a' => "\\a",
		'\b' => "\\b",
		'\f' => "\\f",
		'\n' => "\\n",
		'\r' => "\\r",
		'\t' => "\\t",
		'\v' => "\\v",
		_ when c == quote => "\\" + c,
		// Anything else non-printable, including the line/paragraph separators a C# literal cannot carry.
		_ when char.IsControl(c) || c is '\u0085' or '\u2028' or '\u2029'
			=> "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
		_ => c.ToString()
	};
}
