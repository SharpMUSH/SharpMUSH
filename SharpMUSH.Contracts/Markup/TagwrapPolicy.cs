using System.Collections.Frozen;
using MarkupString.Ansi;
using MarkupString.Html;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// What <c>tagwrap()</c> may write, and what the web portal is willing to render. PennMUSH gates the
/// function on <c>Can_Send_OOB</c> (a wizard, or the Send_OOB power): anyone else is held to
/// <see cref="Tags"/> (<c>src/htmltab.c</c>) and loses the parameters when they name <c>XCH_CMD</c> or
/// <c>SEND</c> (<c>ok_tag_attribute</c>, <c>src/predicat.c:1034</c>).
/// </summary>
/// <remarks>
/// The lists are this game's, not the markup library's: MarkupString supplies the parsing, the policy
/// type and the encoding, and leaves what is safe here to us. A SharpMUSH tag reaches more than a
/// Pueblo client — the portal renders the same markup in a browser — so the unprivileged also lose any
/// attribute a browser runs or lays out, and any address that is not one <see cref="IsSafeAddress"/>
/// accepts. As in PennMUSH, one parameter that fails drops them all rather than leaving a partial tag.
/// </remarks>
public static class TagwrapPolicy
{
	/// <summary>PennMUSH's <c>is_allowed_tag</c> table.</summary>
	public static readonly FrozenSet<string> Tags = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
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
	public static readonly FrozenSet<string> Attributes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
		"align", "alt", "bgcolor", "border", "cellpadding", "cellspacing", "class", "color", "cols", "colspan",
		"face", "height", "href", "lang", "rows", "rowspan", "size", "span", "src", "start", "title", "type",
		"valign", "value", "width", "xch_hint");

	/// <summary>The attributes above whose value a client loads or navigates to.</summary>
	public static readonly FrozenSet<string> AddressAttributes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
		"href", "src");

	/// <summary>
	/// The tag an unprivileged caller gets for <paramref name="tagName"/> and <paramref name="parameters"/>,
	/// or <see langword="null"/> when the tag itself is not allowed. Parameters are parsed and re-encoded
	/// by the markup library; this decides which of them survive.
	/// </summary>
	public static HtmlMarkup? Wrap(string tagName, string parameters)
	{
		if (parameters.Length == 0) return Wrap(tagName, []);

		return HtmlMarkup.TryParseAttributes(parameters, out var parsed)
			? Wrap(tagName, parsed)
			: Wrap(tagName, []);
	}

	/// <summary>
	/// As <see cref="Wrap(string, string)"/>, for attributes already read — the way an HTML parser
	/// hands them over.
	/// </summary>
	public static HtmlMarkup? Wrap(string tagName, IReadOnlyList<HtmlAttribute> attributes)
	{
		if (!Tags.Contains(tagName)) return null;

		return attributes.Count > 0 && attributes.All(Allows)
			? HtmlMarkup.Tag(tagName, [.. attributes])
			: HtmlMarkup.Tag(tagName);
	}

	/// <summary>The policy the portal's HTML rendering is held to, whoever wrote the markup.</summary>
	public static readonly HtmlTagPolicy Portal = HtmlTagPolicy.WellFormed with
	{
		AllowedTags = Tags,
		AllowedAttributes = Attributes,
		UrlAttributes = AddressAttributes,
		OnViolation = HtmlAttributeViolation.DropAllAttributes,
	};

	private static bool Allows(HtmlAttribute attribute) =>
		Attributes.Contains(attribute.Name)
		&& (!AddressAttributes.Contains(attribute.Name) || IsSafeAddress(attribute.Value));

	/// <summary>
	/// An absolute address whose scheme <see cref="UrlSafety"/> accepts, with no control character or
	/// whitespace in it — a browser strips those before reading the scheme, so <c>java&#9;script:</c>
	/// would otherwise pass as a relative address.
	/// </summary>
	private static bool IsSafeAddress(string value) =>
		!value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
		&& Uri.TryCreate(value, UriKind.Absolute, out _)
		&& UrlSafety.IsSafeNavigableUrl(value);
}
