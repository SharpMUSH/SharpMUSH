using System.Globalization;
using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>A RhostMUSH attribute body after the import's rewrites, and one line per change or doubt.</summary>
public sealed record SoftcodeRewriteResult(string Text, IReadOnlyList<string> Notes)
{
	public bool Changed { get; init; }
}

/// <summary>
/// The softcode rewrites a RhostMUSH import makes besides colour (<see cref="ColourSubstitutionRewriter"/>):
/// <c>%&lt;nnn&gt;</c> character codes into the character, <c>timefmt()</c> formats written as literals
/// into SharpMUSH's codes, and calls of RhostMUSH functions into SharpMUSH's (<see cref="RhostFunctionCallRewriter"/>).
/// </summary>
/// <remarks>
/// <para>RhostMUSH reads <c>%&lt;065&gt;</c> (three decimal digits) and <c>%&lt;u0041&gt;</c> (four hex
/// digits) as a character. SharpMUSH reads <c>%&lt;...&gt;</c> as a named argument, so the import writes the
/// character itself, escaped with <c>\</c> when evaluation would read it as syntax, or as <c>%r</c>,
/// <c>%t</c> or <c>%b</c> for a newline, tab or space.</para>
/// <para>RhostMUSH's <c>timefmt()</c> has its own codes (<c>$H</c> is the 12-hour hour, <c>$M</c> the
/// month, <c>$T</c> the minute) with an optional width (<c>$02H</c>). A format given as a literal is
/// rewritten when every code in it has a SharpMUSH counterpart. SharpMUSH's numbers are always two digits
/// wide, so an unpadded RhostMUSH code (<c>$H</c>) gains a leading zero below 10, which the note says.
/// A format that is computed, or uses a code with no counterpart (elapsed times, time zones, lowercase
/// <c>$p</c>, field modifiers), is left as written and reported.</para>
/// </remarks>
public static class RhostSoftcodeRewriter
{
	/// <summary>Rewrites one attribute body.</summary>
	public static SoftcodeRewriteResult Rewrite(string softcode)
	{
		var notes = new List<string>();
		var text = RewriteTimeFormats(softcode, notes);
		text = RhostFunctionCallRewriter.Rewrite(text, notes);
		text = RewriteCharacterCodes(text, notes);
		return new SoftcodeRewriteResult(text, notes) { Changed = text != softcode };
	}

	/// <summary>Every <c>%&lt;nnn&gt;</c> and <c>%&lt;uXXXX&gt;</c> written as the character it stands for.</summary>
	public static string RewriteCharacterCodes(string softcode, List<string> notes)
	{
		if (!softcode.Contains("%<", StringComparison.Ordinal))
		{
			return softcode;
		}

		var output = new StringBuilder(softcode.Length);
		for (var i = 0; i < softcode.Length; i++)
		{
			var ch = softcode[i];
			if (ch == '\\' && i + 1 < softcode.Length)
			{
				output.Append(softcode, i, 2);
				i++;
				continue;
			}

			if (ch != '%' || i + 1 >= softcode.Length)
			{
				output.Append(ch);
				continue;
			}

			if (softcode[i + 1] != '<')
			{
				output.Append(softcode, i, 2);
				i++;
				continue;
			}

			var close = softcode.IndexOf('>', i + 2);
			var inner = close < 0 ? string.Empty : softcode[(i + 2)..close];
			if (CharacterOf(inner) is not { } character)
			{
				notes.Add($"%<{inner}> is not a character code RhostMUSH reads, so it is left as written; SharpMUSH reads it as a named argument");
				output.Append(softcode, i, 2);
				i++;
				continue;
			}

			var written = Written(character);
			notes.Add($"%<{inner}> written as {written}");
			output.Append(written);
			i = close;
		}

		return output.ToString();
	}

	/// <summary>The character a code stands for: three decimal digits up to 255, or <c>u</c> and four hex digits.</summary>
	private static int? CharacterOf(string inner) => inner switch
	{
		{ Length: 3 } when inner.All(char.IsAsciiDigit) && int.Parse(inner, CultureInfo.InvariantCulture) is var code and <= 255 and > 0 => code,
		{ Length: 5 } when inner[0] is 'u' or 'U' && int.TryParse(inner.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex)
			&& hex > 0 && !char.IsSurrogate((char)hex) => hex,
		_ => null
	};

	/// <summary>A character as softcode writes it so that evaluation gives the character back.</summary>
	private static string Written(int code) => code switch
	{
		'\n' => "%r",
		'\r' => string.Empty,
		'\t' => "%t",
		' ' => "%b",
		'%' or '\\' or '[' or ']' or '{' or '}' or '(' or ')' or ',' or ';' or '=' or '$' or '^' or ':' => "\\" + (char)code,
		< 32 or 127 => $"[chr({code})]",
		_ => ((char)code).ToString()
	};

	/// <summary>Every <c>timefmt()</c> whose format is a literal, with RhostMUSH's codes written as SharpMUSH's.</summary>
	public static string RewriteTimeFormats(string softcode, List<string> notes)
	{
		var start = 0;
		var output = new StringBuilder(softcode.Length);
		var found = softcode.IndexOf("timefmt(", StringComparison.OrdinalIgnoreCase);
		while (found >= 0)
		{
			var argumentStart = found + "timefmt(".Length;
			if (found > 0 && (char.IsAsciiLetterOrDigit(softcode[found - 1]) || softcode[found - 1] == '_'))
			{
				found = softcode.IndexOf("timefmt(", argumentStart, StringComparison.OrdinalIgnoreCase);
				continue;
			}

			var argumentEnd = FirstArgumentEnd(softcode, argumentStart);
			var format = softcode[argumentStart..argumentEnd];
			output.Append(softcode, start, argumentStart - start);
			switch (TranslateFormat(format))
			{
				case TimeFormatTranslation { Format: var translated, Padded: var padded }:
					output.Append(translated);
					if (translated != format)
					{
						notes.Add($"timefmt() format {format} written as {translated}" +
							(padded ? "; numbers below 10 now print with a leading zero" : string.Empty));
					}

					break;

				case TimeFormatRefusal { Reason: var reason }:
					output.Append(format);
					notes.Add($"timefmt() format {format} left as written: {reason}");
					break;
			}

			start = argumentEnd;
			found = softcode.IndexOf("timefmt(", argumentEnd, StringComparison.OrdinalIgnoreCase);
		}

		return start == 0 ? softcode : output.Append(softcode, start, softcode.Length - start).ToString();
	}

	/// <summary>Where the first argument of a call ends: its <c>,</c> or <c>)</c> at depth zero.</summary>
	private static int FirstArgumentEnd(string text, int from)
	{
		var depth = 0;
		for (var i = from; i < text.Length; i++)
		{
			switch (text[i])
			{
				case '\\' or '%':
					i++;
					break;
				case '(' or '[' or '{':
					depth++;
					break;
				case ')' or ']' or '}' when depth > 0:
					depth--;
					break;
				case ',' or ')' when depth == 0:
					return i;
			}
		}

		return text.Length;
	}

	private sealed record TimeFormatTranslation(string Format, bool Padded);

	private sealed record TimeFormatRefusal(string Reason);

	private static object TranslateFormat(string format)
	{
		if (format.IndexOfAny(['%', '[', ']', '(', ')', '{', '}', '\\']) >= 0)
		{
			return new TimeFormatRefusal("the format is computed, so its codes cannot be read at import");
		}

		var output = new StringBuilder(format.Length);
		var padded = false;
		for (var i = 0; i < format.Length; i++)
		{
			if (format[i] != '$')
			{
				output.Append(format[i]);
				continue;
			}

			var j = i + 1;
			if (j < format.Length && format[j] == '$')
			{
				output.Append("$$");
				i = j;
				continue;
			}

			if (j < format.Length && format[j] is '-' or '~' or '@' or '#' or '!')
			{
				return new TimeFormatRefusal($"the field modifier ${format[j]} has no SharpMUSH counterpart");
			}

			var zeroPad = j < format.Length && format[j] == '0';
			var digits = j;
			while (digits < format.Length && char.IsAsciiDigit(format[digits])) digits++;
			var width = digits > j ? int.Parse(format.AsSpan(j, digits - j), CultureInfo.InvariantCulture) : 0;
			if (digits >= format.Length)
			{
				return new TimeFormatRefusal("it ends in an unfinished code");
			}

			var code = format[digits];
			switch (Translate(code, zeroPad, width))
			{
				case (string penn, var padding):
					output.Append('$').Append(penn);
					padded |= padding;
					break;
				default:
					return new TimeFormatRefusal($"${format[(i + 1)..(digits + 1)]} has no SharpMUSH counterpart");
			}

			i = digits;
		}

		return new TimeFormatTranslation(output.ToString(), padded);
	}

	/// <summary>
	/// A RhostMUSH code and its width as the SharpMUSH code that prints the same, and whether SharpMUSH pads
	/// where RhostMUSH did not; <c>null</c> for none.
	/// </summary>
	private static (string Code, bool Padded)? Translate(char code, bool zeroPad, int width)
	{
		var exactPad = zeroPad && width == 2;
		var noWidth = width == 0;
		return code switch
		{
			'a' or 'A' or 'b' or 'B' when noWidth => (code.ToString(), false),
			'P' when noWidth => ("p", false),
			'W' when noWidth || width == 1 => ("w", false),
			'Y' when noWidth || width <= 4 => ("Y", false),
			'M' when noWidth || exactPad => ("m", noWidth),
			'D' when noWidth || exactPad => ("d", noWidth),
			'h' when noWidth || exactPad => ("H", noWidth),
			'H' when noWidth || exactPad => ("I", noWidth),
			'T' when noWidth || exactPad => ("M", noWidth),
			'S' when noWidth || exactPad => ("S", noWidth),
			'y' when noWidth || exactPad => ("y", noWidth),
			'J' when noWidth || (zeroPad && width == 3) => ("j", noWidth),
			_ => null
		};
	}
}
