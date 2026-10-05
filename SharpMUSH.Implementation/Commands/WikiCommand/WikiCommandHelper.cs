using SharpMUSH.Library.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// Shared helpers for @wiki subcommands and wiki() functions:
/// wiki-link references, edit-permission checks, and listing line formatting.
/// </summary>
public static class WikiCommandHelper
{
	/// <summary>
	/// The <c>@wiki</c> page reference a <c>[[wiki link]]</c> points at, or the empty string when the
	/// link carries no usable identity.
	/// </summary>
	/// <remarks>
	/// <see cref="WikiLinkInline.Slug"/> is the canonical <c>namespace/slug</c> path identity;
	/// <c>@wiki</c> spells the same identity <c>namespace:slug</c>, which is also what
	/// <see cref="DisplayReference"/> prints. Only the first separator is rewritten, so a slug that
	/// itself contains <c>/</c> survives and the result round-trips through <see cref="WikiHelpers.ResolveTitle"/>
	/// back to the page the link named.
	/// </remarks>
	public static string ReferenceForWikiLink(WikiLinkInline wikiLink)
	{
		var parts = (wikiLink.Slug ?? string.Empty).Split('/', 2);
		return parts.Length == 2 && parts.All(part => part.Length > 0)
			? string.Join(':', parts)
			: string.Empty;
	}

	/// <summary>
	/// Splits a <em>write</em> target of the form <c>&lt;page&gt;/&lt;lang&gt;</c> into the page reference
	/// (still to be passed to <see cref="WikiHelpers.ResolveTitle"/>) and a canonicalised BCP-47 locale tag.
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
	public static Result<LocaleTarget> SplitLocaleTarget(string target)
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

		return WikiHelpers.NormalizeLocale(parts[1].Trim()) switch
		{
			string locale => new LocaleTarget(pageTarget, locale),
			Error<string> error => error
		};
	}

	/// <summary>
	/// A write target split by <see cref="SplitLocaleTarget"/>: the page reference and its canonical locale tag.
	/// </summary>
	public readonly record struct LocaleTarget(string PageTarget, string Locale);

	/// <summary>
	/// The display form of a page reference, always fully qualified as "ns:slug". Round-trips
	/// through <see cref="WikiHelpers.ResolveTitle"/>, so every identifier a listing prints can be pasted straight
	/// back into <c>@wiki</c>.
	/// </summary>
	/// <remarks>
	/// Main-namespace pages used to print bare ("home") while everything else printed qualified
	/// ("help:markdown_guide"), which put two identifier grammars in one column of one listing
	/// and left the reader to work out which spelling a given row was in. One grammar for every row is
	/// the cost of one prefix on the common case.
	/// </remarks>
	public static string DisplayReference(WikiPage page) =>
		$"{page.Namespace}:{page.Slug}";

	/// <summary>The one service that decides wiki permissions, shared with the portal and the wiki functions.</summary>
	public static IWikiAccessService Access(IMUSHCodeParser parser)
		=> parser.ServiceProvider.GetRequiredService<IWikiAccessService>();

	/// <summary>The executor as a wiki reader: what its roles grant, and its dbref as authors are stored.</summary>
	public static ValueTask<WikiReader> ReaderAsync(IMUSHCodeParser parser, AnySharpObject executor)
		=> Access(parser).ForObjectAsync(executor);

	/// <summary>The pages a listing shows the executor, applied by the store before it pages.</summary>
	public static async ValueTask<WikiVisibility> VisibilityAsync(IMUSHCodeParser parser, AnySharpObject executor)
		=> await Access(parser).VisibilityAsync(await ReaderAsync(parser, executor));

	/// <summary>
	/// The pages softcode run by <paramref name="executor"/> may reach: what it may read, and never a draft,
	/// its own included. A function result is copied into attributes and messages anywhere, so a draft does
	/// not leave the page through one.
	/// </summary>
	public static async ValueTask<WikiVisibility> SoftcodeVisibilityAsync(IMUSHCodeParser parser, AnySharpObject executor)
		=> await VisibilityAsync(parser, executor) with { IncludeDrafts = false, AuthorDbref = null };

	/// <summary>
	/// Whether the executor may take <paramref name="action"/> on <paramref name="page"/>; when not, tells it
	/// why ("WIKI: You can't edit 'Title': category lore requires lore.edit."), and returns the refusal to
	/// hand back. A page the executor may not read gets the same answer as a page that does not exist, so
	/// the refusal does not disclose it.
	/// </summary>
	public static async ValueTask<MString?> RefusalAsync(IMUSHCodeParser parser, INotifyService notifyService,
		AnySharpObject executor, WikiPage page, WikiAction action, string targetText)
	{
		var access = Access(parser);
		var reader = await ReaderAsync(parser, executor);
		if (!(await access.DecideAsync(reader, page, WikiAction.Read)).Allowed)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetText.Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var decision = await access.DecideAsync(reader, page, action);
		if (decision.Allowed) return null;

		await notifyService.Notify(executor,
			$"WIKI: You can't {action.ToString().ToLowerInvariant()} '{page.Title}': {decision.Describe()}.", executor);
		return MarkupText.Plain(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary>
	/// True when this reader may see other people's unpublished (draft) pages and unpublished translations:
	/// the <c>wiki.drafts</c> permission, which the portal checks too. The <c>includeDrafts</c> argument every
	/// <c>IWikiLocalizationService</c> read takes.
	/// </summary>
	public static async ValueTask<bool> CanSeeDrafts(IMUSHCodeParser parser, AnySharpObject executor)
		=> (await ReaderAsync(parser, executor)).Has(PortalPermission.WikiDrafts);

	/// <summary>The executor's dbref string as stored in wiki author/editor fields.</summary>
	public static string EditorDbref(AnySharpObject executor) =>
		$"#{executor.Object().Key}";

	/// <summary>One listing line: "reference — Title (rev N, yyyy-MM-dd)" plus a draft marker.</summary>
	public static string FormatPageLine(WikiPage page)
	{
		var markers = page.Published ? "" : " (draft)";
		return $"{DisplayReference(page),-30} {page.Title} (rev {page.RevisionNumber}, {page.UpdatedAt:yyyy-MM-dd}){markers}";
	}

	/// <summary>
	/// One listing line for a page resolved into a locale. Identical to the
	/// <see cref="FormatPageLine(WikiPage)"/> overload except that the title, revision number and
	/// published marker are the served locale's rather than the source's.
	/// </summary>
	public static string FormatPageLine(LocalizedWikiPage page)
	{
		var markers = page.Published ? "" : " (draft)";
		return $"{DisplayReference(page.Page),-30} {page.Title} (rev {page.RevisionNumber}, {page.UpdatedAt:yyyy-MM-dd}){markers}";
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

		var database = parser.ServiceProvider.GetRequiredService<IAttributeStore>();
		return await database.GetAttributeAsync(executor.Object().DBRef, ["LOCALE"], CancellationToken.None)
			.Select(attr => attr.Value.ToPlainText())
			.FirstOrDefaultAsync(saved => !string.IsNullOrEmpty(saved));
	}
}
