using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// MUSHcode rendering for the package review UI (Phase 8, decision 20.9):
/// a lightweight tokenizer that wraps recognizable constructs in
/// <c>&lt;span class="mush-*"&gt;</c> elements, plus the dangerous-pattern
/// scanner (decision 20.8 — advisory flags, never blockers).
/// </summary>
/// <remarks>
/// Token classes emitted: <c>mush-cmdpattern</c> ($pattern: prefixes),
/// <c>mush-fn</c> (function names before '('), <c>mush-sub</c> (%-substitutions),
/// <c>mush-dbref</c> (#123 / #123:456), <c>mush-ref</c> ({{package refs}}),
/// <c>mush-atcmd</c> (@commands), <c>mush-danger</c> (wraps dangerous matches).
/// All other text is HTML-encoded verbatim. This is a display aid, not a
/// parser — ambiguity resolves toward plain text.
/// </remarks>
public static partial class MushcodeHighlighter
{
	[GeneratedRegex(@"^\$(?<pattern>[^:]+):", RegexOptions.Singleline)]
	private static partial Regex CommandPatternRegex();

	// Function name immediately followed by an open paren: u(, switch(, etc.
	[GeneratedRegex(@"\G(?<name>[a-zA-Z_][a-zA-Z0-9_]*)\(")]
	private static partial Regex FunctionCallRegex();

	// %-substitutions: %0-%9, %#, %!, %@, %?, %q0/%qa, %q<name>, %va-%vz, %wa.., %xa.., %b %r %t %s %o %p %n etc.
	[GeneratedRegex(@"\G%(?:q<[^>]*>|q[0-9a-zA-Z]|v[0-9a-zA-Z]|w[0-9a-zA-Z]|x[0-9a-zA-Z]|[0-9#!@?bBrRtTsSoOpPnNaAcCiIlLuU+=])")]
	private static partial Regex SubstitutionRegex();

	[GeneratedRegex(@"\G#-?\d+(?::\d+)?")]
	private static partial Regex DbrefRegex();

	[GeneratedRegex(@"\G\{\{[^{}]*\}\}")]
	private static partial Regex MustacheRegex();

	[GeneratedRegex(@"\G@[a-zA-Z][a-zA-Z0-9_/]*")]
	private static partial Regex AtCommandRegex();

	/// <summary>
	/// The advisory dangerous patterns (design doc list). Matched
	/// case-insensitively anywhere in a value.
	/// </summary>
	public static readonly IReadOnlyList<string> DangerousPatterns =
	[
		"@force", "@toad", "@newpassword", "@nuke", "@boot",
		"@pcreate", "@halt", "@wall", "@shutdown", "@chown", "@power", "pemit(*"
	];

	[GeneratedRegex(@"@(?:force|toad|newpassword|nuke|boot|pcreate|halt|wall|shutdown|chown|power)\b|pemit\(\s*\*",
		RegexOptions.IgnoreCase)]
	private static partial Regex DangerRegex();

	/// <summary>Returns the distinct dangerous patterns found in <paramref name="value"/> (empty when clean).</summary>
	public static IReadOnlyList<string> FindDangerousPatterns(string value) =>
		DangerRegex().Matches(value)
			.Select(m => m.Value.ToLowerInvariant().Replace(" ", ""))
			.Distinct(StringComparer.Ordinal)
			.ToList();

	/// <summary>
	/// Renders MUSHcode to highlighted HTML. Output contains only
	/// HTML-encoded text and span elements — safe to embed via MarkupString.
	/// </summary>
	public static string ToHtml(string value)
	{
		var builder = new StringBuilder(value.Length * 2);
		var dangerSpans = DangerRegex().Matches(value)
			.Select(m => (Start: m.Index, End: m.Index + m.Length))
			.ToList();

		var position = 0;

		// Leading $pattern: gets its own class.
		var command = CommandPatternRegex().Match(value);
		if (command.Success)
		{
			Append(builder, "mush-cmdpattern", value[..command.Length], dangerSpans, 0);
			position = command.Length;
		}

		while (position < value.Length)
		{
			Match match;
			if ((match = MustacheRegex().Match(value, position)).Success)
			{
				Append(builder, "mush-ref", match.Value, dangerSpans, position);
			}
			else if ((match = SubstitutionRegex().Match(value, position)).Success)
			{
				Append(builder, "mush-sub", match.Value, dangerSpans, position);
			}
			else if ((match = DbrefRegex().Match(value, position)).Success)
			{
				Append(builder, "mush-dbref", match.Value, dangerSpans, position);
			}
			else if ((match = AtCommandRegex().Match(value, position)).Success)
			{
				Append(builder, "mush-atcmd", match.Value, dangerSpans, position);
			}
			else if ((match = FunctionCallRegex().Match(value, position)).Success)
			{
				// Highlight the name; the paren stays plain.
				Append(builder, "mush-fn", match.Groups["name"].Value, dangerSpans, position);
				builder.Append('(');
			}
			else
			{
				AppendPlain(builder, value[position].ToString(), dangerSpans, position);
				position++;
				continue;
			}

			position += match.Length;
		}

		return builder.ToString();
	}

	private static void Append(
		StringBuilder builder, string cssClass, string text,
		List<(int Start, int End)> dangerSpans, int position)
	{
		var encoded = WebUtility.HtmlEncode(text);
		builder.Append(InDanger(dangerSpans, position)
			? $"<span class=\"{cssClass} mush-danger\">{encoded}</span>"
			: $"<span class=\"{cssClass}\">{encoded}</span>");
	}

	private static void AppendPlain(
		StringBuilder builder, string text, List<(int Start, int End)> dangerSpans, int position)
	{
		var encoded = WebUtility.HtmlEncode(text);
		builder.Append(InDanger(dangerSpans, position)
			? $"<span class=\"mush-danger\">{encoded}</span>"
			: encoded);
	}

	private static bool InDanger(List<(int Start, int End)> spans, int position) =>
		spans.Any(s => position >= s.Start && position < s.End);
}
