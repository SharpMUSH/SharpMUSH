using MarkupString.MarkupImplementation;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// The renderer <c>@wiki</c> displays a page body with: identical to the shared
/// <see cref="RecursiveMarkdownRenderer"/> in every respect except that a <c>[[Page Name]]</c> wiki
/// link becomes a clickable command link running <c>@wiki &lt;page&gt;</c>.
/// </summary>
/// <remarks>
/// The base renderer drops a wiki link's target because a help file or a <c>rendermarkdown()</c>
/// call has nowhere to send the reader. Under <c>@wiki</c> it has: <c>@wiki &lt;page&gt;</c> is
/// exactly how a terminal session navigates the wiki, so the link is followable there — and only
/// there.
/// <para>
/// A subclass rather than an option on the base type, for the same reason
/// <see cref="SharpMUSH.Implementation.Functions.CustomizableMarkdownRenderer"/> is one: the
/// rendering policy belongs to the surface that displays the page, not to the shared renderer.
/// <c>wiki()</c>, <c>rendermarkdown()</c> and <c>rendermarkdowncustom()</c> hand their output to
/// softcode that may present it however it likes, so their wiki links keep the neutral styling a
/// game can override for itself.
/// </para>
/// </remarks>
public sealed class WikiCommandRenderer(int maxWidth = 78, IMUSHCodeParser? mushParser = null)
	: RecursiveMarkdownRenderer(maxWidth, mushParser)
{
	/// <inheritdoc/>
	protected override MString RenderWikiLink(WikiLinkInline wikiLink)
	{
		var text = wikiLink.DisplayText ?? wikiLink.Title;
		if (string.IsNullOrWhiteSpace(text)) return MModule.empty();

		var reference = PageReference(wikiLink.Slug);
		// No usable target: fall back to the base renderer's styled prose rather than emitting a
		// command link that would run "@wiki" with nothing after it.
		if (reference.Length == 0) return base.RenderWikiLink(wikiLink);

		// Underlined for exactly the reason the base renderer underlines. ApplyDetails ignores a
		// command link — OSC 8 can only navigate — so a plain telnet client's bytes are unchanged,
		// and only Pueblo (XCH_CMD), MXP (SEND) and the web terminal (xch_cmd) gain the click.
		var command = $"@wiki {reference}";
		return MModule.MarkupSingle(
			Ansi.Create(linkUrl: command, linkKind: LinkKind.Command, linkText: command, underlined: true),
			text);
	}

	/// <summary>
	/// The <c>@wiki</c> page reference for a wiki link's canonical identity, or the empty string when
	/// the link has none.
	/// </summary>
	/// <remarks>
	/// <see cref="WikiLinkInline.Slug"/> is <c>namespace/category/slug</c>; <c>@wiki</c> spells the same
	/// identity <c>namespace:category:slug</c>, the fully qualified form every wiki listing already
	/// prints (see <see cref="WikiCommandHelper.DisplayReference"/>). Only the first two separators are
	/// rewritten, so a slug that itself contains <c>/</c> survives and the reference round-trips through
	/// <see cref="WikiCommandHelper.ResolveTarget"/> back to the page the link named.
	/// </remarks>
	private static string PageReference(string canonicalSlug)
	{
		var parts = canonicalSlug.Split('/', 3);
		return parts.Length == 3 && parts.All(p => p.Length > 0)
			? string.Join(':', parts)
			: string.Empty;
	}
}
