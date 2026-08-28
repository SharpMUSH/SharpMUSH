using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// Shared helpers for @wiki subcommands and wiki() functions:
/// page-target resolution ("Help:Getting Started" → namespace + slug),
/// edit-permission checks, and listing line formatting.
/// </summary>
public static class WikiCommandHelper
{
	/// <summary>
	/// Resolves a user-supplied page target into a (namespace, category, slug) identity.
	/// Accepts the same forms as <c>[[wiki links]]</c>: a bare title ("Getting Started"),
	/// a namespace-prefixed one ("Help:Getting Started" → category general), or a fully
	/// qualified one ("Help:Guides:Getting Started"). Unknown namespace prefixes are treated
	/// as part of a Main-namespace title.
	/// </summary>
	public static (WikiNamespace Namespace, string Category, string Slug) ResolveTarget(string target)
	{
		var trimmed = target.Trim();
		var parts = trimmed.Split(':', 3);

		if (parts.Length == 3
			&& Enum.TryParse<WikiNamespace>(parts[0].Trim(), ignoreCase: true, out var ns3))
		{
			return (ns3, WikiHelpers.NormalizeCategory(parts[1]), WikiHelpers.Slugify(parts[2].Trim()));
		}

		if (parts.Length == 2
			&& Enum.TryParse<WikiNamespace>(parts[0].Trim(), ignoreCase: true, out var ns2))
		{
			return (ns2, WikiHelpers.DefaultCategory, WikiHelpers.Slugify(parts[1].Trim()));
		}

		return (WikiNamespace.Main, WikiHelpers.DefaultCategory, WikiHelpers.Slugify(trimmed));
	}

	/// <summary>
	/// The <c>@wiki</c> page reference a <c>[[wiki link]]</c> points at, or the empty string when the
	/// link carries no usable identity.
	/// </summary>
	/// <remarks>
	/// <see cref="WikiLinkInline.Slug"/> is the canonical <c>namespace/category/slug</c> path identity;
	/// <c>@wiki</c> spells the same identity <c>namespace:category:slug</c>, which is also what
	/// <see cref="DisplayReference"/> prints. Only the first two separators are rewritten, so a slug that
	/// itself contains <c>/</c> survives and the result round-trips through <see cref="ResolveTarget"/>
	/// back to the page the link named.
	/// </remarks>
	public static string ReferenceForWikiLink(WikiLinkInline wikiLink)
	{
		var parts = (wikiLink.Slug ?? string.Empty).Split('/', 3);
		return parts.Length == 3 && parts.All(part => part.Length > 0)
			? string.Join(':', parts)
			: string.Empty;
	}

	/// <summary>
	/// Splits a <em>write</em> target of the form <c>&lt;page&gt;/&lt;lang&gt;</c> into the page reference
	/// (still to be passed to <see cref="ResolveTarget"/>) and a canonicalised BCP-47 locale tag.
	/// </summary>
	/// <remarks>
	/// The locale a translation is written into is explicit and mandatory: an absent, ambiguous or
	/// unrecognised tag is an error naming the problem, never a silent default and never the executor's
	/// <c>LOCALE</c>. Reads may guess — a wrong guess shows the reader the wrong translation, which is
	/// visible and recoverable. A wrong guess on a write files English prose as the French translation,
	/// which is neither.
	/// <para>
	/// <c>/</c> is the separator because it is the PennMUSH convention for naming a sub-part of a target
	/// (<c>@set obj/attr=…</c>) and because it keeps the whole right-hand side content: nothing scans the
	/// translated prose for a leading tag, so no French sentence opening with "De" can be mistaken for a
	/// request to write German. A target carrying more than one <c>/</c> is refused rather than split on a
	/// chosen occurrence, so an unexpected shape stops the write instead of guessing at it.
	/// </para>
	/// </remarks>
	public static OneOf<(string PageTarget, string Locale), Error<string>> SplitLocaleTarget(string target)
	{
		var parts = target.Trim().Split('/');

		switch (parts.Length)
		{
			case 1:
				return new Error<string>(
					"a translation needs an explicit language: @wiki/translate <page>/<lang>=<text>");
			case > 2:
				return new Error<string>(
					$"'{target.Trim()}' has more than one '/'; the form is <page>/<lang>.");
		}

		var pageTarget = parts[0].Trim();
		if (pageTarget.Length == 0)
			return new Error<string>("that names a language but no page: @wiki/translate <page>/<lang>=<text>");

		var locale = WikiHelpers.NormalizeLocale(parts[1].Trim());
		return locale.IsT1
			? locale.AsT1
			: (pageTarget, locale.AsT0);
	}

	/// <summary>
	/// The display form of a page reference, always fully qualified as "ns:category:slug". Round-trips
	/// through <see cref="ResolveTarget"/>, so every identifier a listing prints can be pasted straight
	/// back into <c>@wiki</c>.
	/// </summary>
	/// <remarks>
	/// Main-namespace pages used to print bare ("home") while everything else printed qualified
	/// ("help:general:markdown_guide"), which put two identifier grammars in one column of one listing
	/// and left the reader to work out which spelling a given row was in. One grammar for every row is
	/// the cost of one prefix on the common case.
	/// </remarks>
	public static string DisplayReference(WikiPage page) =>
		$"{page.Namespace}:{page.Category ?? WikiHelpers.DefaultCategory}:{page.Slug}";

	/// <summary>
	/// Edit permission mirrors the web rule: protected pages are Wizard-only;
	/// everything else is editable by any player.
	/// </summary>
	public static async ValueTask<bool> CanEdit(AnySharpObject executor, WikiPage page) =>
		!page.IsProtected || await executor.IsWizard();

	/// <summary>
	/// True when this reader may see unpublished (draft) pages and unpublished translations. The in-game
	/// counterpart of the portal's <c>wiki.read</c> scope, and the <c>includeDrafts</c> argument every
	/// <c>IWikiLocalizationService</c> read takes.
	/// </summary>
	/// <remarks>
	/// Deliberately <em>not</em> <see cref="CanEdit"/>. That rule grants every player edit rights on every
	/// unprotected page, so using it here would gate nothing: an unpublished page stays a draft precisely
	/// because a wizard unpublished it (<c>@wiki/publish</c> and <c>@wiki/unpublish</c> are wizard-only),
	/// and the wizard bit is therefore the only in-game distinction that tracks who is allowed to know a
	/// draft exists.
	/// </remarks>
	public static ValueTask<bool> CanSeeDrafts(AnySharpObject executor) => executor.IsWizard();

	/// <summary>The executor's dbref string as stored in wiki author/editor fields.</summary>
	public static string EditorDbref(AnySharpObject executor) =>
		$"#{executor.Object().Key}";

	/// <summary>One listing line: "reference — Title (rev N, yyyy-MM-dd)" plus draft/protected markers.</summary>
	public static string FormatPageLine(WikiPage page)
	{
		var markers = $"{(page.Published ? "" : " (draft)")}{(page.IsProtected ? " (protected)" : "")}";
		return $"{DisplayReference(page),-30} {page.Title} (rev {page.RevisionNumber}, {page.UpdatedAt:yyyy-MM-dd}){markers}";
	}

	/// <summary>
	/// One listing line for a page resolved into a locale. Identical to the
	/// <see cref="FormatPageLine(WikiPage)"/> overload except that the title, revision number and
	/// published marker are the served locale's rather than the source's.
	/// </summary>
	public static string FormatPageLine(LocalizedWikiPage page)
	{
		var markers = $"{(page.Published ? "" : " (draft)")}{(page.Page.IsProtected ? " (protected)" : "")}";
		return $"{DisplayReference(page.Page),-30} {page.Title} (rev {page.RevisionNumber}, {page.Page.UpdatedAt:yyyy-MM-dd}){markers}";
	}

	/// <summary>
	/// The executor's locale for wiki reads: the connection's <c>Locale</c> metadata when the command came
	/// from a real connection, otherwise the persisted <c>LOCALE</c> attribute (the <c>@force</c> case).
	/// Returns null when neither is set, which <c>IWikiLocalizationService</c> reads as "use the configured
	/// default" — the same contract <c>?lang=</c> has on the web side.
	/// </summary>
	/// <remarks>
	/// Mirrors the read in <c>Commands.SetLocale</c> (<c>MoreCommands.cs</c>) rather than inventing a second
	/// source of truth for what locale a player is on.
	/// </remarks>
	public static async ValueTask<string?> ResolveExecutorLocaleAsync(
		IMUSHCodeParser parser, AnySharpObject executor)
	{
		var handle = parser.CurrentState.Handle;
		if (handle.HasValue)
		{
			var connectionService = parser.ServiceProvider.GetRequiredService<IConnectionService>();
			var conn = connectionService.Get(handle.Value);
			if (conn is not null
				&& conn.Metadata.TryGetValue("Locale", out var stored)
				&& !string.IsNullOrEmpty(stored))
				return stored;
		}

		var database = parser.ServiceProvider.GetRequiredService<ISharpDatabase>();
		var localeAttrs = database.GetAttributeAsync(executor.Object().DBRef, ["LOCALE"], CancellationToken.None);
		await foreach (var attr in localeAttrs)
		{
			var saved = attr.Value.ToPlainText();
			if (!string.IsNullOrEmpty(saved)) return saved;
		}

		return null;
	}
}
