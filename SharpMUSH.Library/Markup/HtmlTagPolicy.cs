using System.Collections.Frozen;
using System.Net;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// What <c>tagwrap()</c> may write. PennMUSH gates the function on <c>Can_Send_OOB</c> (a wizard, or the
/// Send_OOB power): anyone else is held to <see cref="AllowedTags"/> (<c>src/htmltab.c</c>) and loses
/// the parameters when they name <c>XCH_CMD</c> or <c>SEND</c> (<c>ok_tag_attribute</c>,
/// <c>src/predicat.c:1034</c>).
///
/// <para>SharpMUSH's tag reaches more than a Pueblo client: the web portal renders the same markup in a
/// browser. So for the unprivileged the parameters also have to be ones a browser cannot be made to
/// run — every attribute on <see cref="AllowedAttributes"/>, and every URL one naming a scheme
/// <see cref="UrlSafety"/> accepts — and they are written back quoted and encoded, never as given. As
/// in PennMUSH, one parameter that fails drops them all rather than leaving a partial tag.</para>
/// </summary>
public static class HtmlTagPolicy
{
	/// <summary>PennMUSH's <c>is_allowed_tag</c> table.</summary>
	public static readonly FrozenSet<string> AllowedTags = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
		"A", "ABBR", "ACRONYM", "ADDRESS", "B", "BIG", "BLOCKQUOTE", "BR", "CAPTION", "CENTER", "CITE",
		"CODE", "COL", "COLGROUP", "DD", "DEL", "DFN", "DIR", "DIV", "DL", "DT", "EM", "FONT", "H1", "H2",
		"H3", "H4", "H5", "H6", "HR", "I", "IMG", "INS", "KBD", "LI", "MENU", "OL", "P", "PRE", "S", "SAMP",
		"SMALL", "SPAN", "STRIKE", "STRONG", "SUB", "SUP", "TABLE", "TBODY", "TD", "TFOOT", "TH", "THEAD",
		"TR", "TT", "U", "UL", "VAR");

	/// <summary>
	/// The presentational attributes the tags above take. Event handlers (<c>on*</c>) and <c>style</c>
	/// are absent because a browser runs or lays them out, and <c>XCH_CMD</c> and <c>SEND</c> because
	/// PennMUSH withholds them: a command link is <c>cmdlink()</c>'s, behind the same power.
	/// </summary>
	public static readonly FrozenSet<string> AllowedAttributes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
		"align", "alt", "bgcolor", "border", "cellpadding", "cellspacing", "class", "color", "cols", "colspan",
		"face", "height", "href", "lang", "rows", "rowspan", "size", "span", "src", "start", "title", "type",
		"valign", "value", "width", "xch_hint");

	private static readonly FrozenSet<string> UrlAttributes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
		"href", "src");

	/// <summary>
	/// A tag name is letters then letters or digits. Anything else — a space, a quote, a <c>&gt;</c> —
	/// would let the name carry attributes or close the tag, whoever is asking.
	/// </summary>
	public static bool IsTagName(string name) =>
		name.Length is > 0 and <= 16 && char.IsAsciiLetter(name[0]) && name.All(char.IsAsciiLetterOrDigit);

	/// <summary>
	/// The parameters an unprivileged caller's tag keeps: every <c>name=value</c> pair re-quoted and
	/// encoded, or <see langword="null"/> when any pair is malformed or not allowed.
	/// </summary>
	public static string? Sanitize(string parameters)
	{
		List<string> written = [];
		var rest = parameters.AsSpan().Trim();
		while (!rest.IsEmpty)
		{
			var equals = rest.IndexOf('=');
			if (equals <= 0)
			{
				return null;
			}

			var name = rest[..equals].Trim().ToString();
			rest = rest[(equals + 1)..].TrimStart();

			if (!TakeValue(ref rest, out var value)
				|| !AllowedAttributes.Contains(name)
				|| (UrlAttributes.Contains(name) && !IsSafeUrl(value)))
			{
				return null;
			}

			written.Add($"{name}=\"{WebUtility.HtmlEncode(value)}\"");
			rest = rest.TrimStart();
		}

		return string.Join(' ', written);
	}

	private static bool TakeValue(ref ReadOnlySpan<char> rest, out string value)
	{
		if (rest.IsEmpty)
		{
			value = string.Empty;
			return false;
		}

		if (rest[0] is '"' or '\'')
		{
			var close = rest[1..].IndexOf(rest[0]);
			if (close < 0)
			{
				value = string.Empty;
				return false;
			}

			value = rest.Slice(1, close).ToString();
			rest = rest[(close + 2)..];
			return true;
		}

		var end = rest.IndexOfAny(' ', '\t');
		value = (end < 0 ? rest : rest[..end]).ToString();
		rest = end < 0 ? [] : rest[end..];
		return true;
	}

	/// <summary>
	/// An absolute address whose scheme <see cref="UrlSafety"/> accepts, with no control character or
	/// whitespace a browser would strip before reading the scheme (<c>java&#9;script:</c>).
	/// </summary>
	private static bool IsSafeUrl(string url) =>
		!url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
		&& Uri.TryCreate(url, UriKind.Absolute, out _)
		&& UrlSafety.IsSafeNavigableUrl(url);
}
