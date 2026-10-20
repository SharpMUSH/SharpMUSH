using System.Security.Claims;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// One localization and one final corroboration for a landing or an identity-constrained article.
/// These ordinary reads do not form a coherent database/authority snapshot and never retry.
/// </summary>
public sealed class WikiLandingReadService(IWikiService wiki, IPackageRegistryService packages,
	IWikiLocalizationService localization, IWikiLocaleResolver locales, IWikiAccessService access,
	WikiLandingReaderResolver readers, IWikiNameResolver names)
{
	private sealed record Target(string Namespace, string Slug, string PageId, string? PackageId);
	private sealed record Candidate(string Locale, string Title, bool Published, int Revision, DateTimeOffset UpdatedAt);
	private sealed record Prepared(Target Target, WikiPage Source, string SourceLocale, string DefaultLocale,
		string RequestedLocale, string? RawRequest, bool IncludeDrafts, Candidate[] Candidates, LocalizedWikiPage Selection);

	public async Task<Found<WikiLandingPageDto>> LandingAsync(ClaimsPrincipal user, string collection, string? lang, CancellationToken ct)
	{
		if (!collection.StartsWith("package:", StringComparison.Ordinal) || collection.Length == "package:".Length)
			return new NotFound();
		var packageId = collection["package:".Length..];
		if (await packages.GetInstalledPackageAsync(packageId) is not InstalledPackageRecord installed
			|| installed.Id != packageId || installed.WikiLandingPage is not { } binding)
			return new NotFound();
		var target = new Target(binding.Namespace, binding.Slug, binding.PageId, packageId);
		if (await PrepareAsync(user, target, lang, ct) is not Prepared prepared) return new NotFound();
		var selected = prepared.Selection;
		var dto = new WikiLandingPageDto(target.PageId, target.Namespace, target.Slug, selected.Title, selected.Locale, selected.IsFallback);
		if (await FinalizeAsync(user, prepared, ct) is not WikiAccess) return new NotFound();
		return dto;
	}

	public async Task<Found<WikiPageDto>> ArticleAsync(ClaimsPrincipal user, WikiNamespace ns, string slug,
		string expectedPageId, string? lang, CancellationToken ct)
	{
		var target = new Target(WikiHelpers.NamespaceName(ns), slug, expectedPageId, null);
		if (await PrepareAsync(user, target, lang, ct) is not Prepared prepared) return new NotFound();
		var selected = prepared.Selection;
		var source = prepared.Source;
		// Complete every payload-decoration await before the final authority decision.
		var requirements = await access.RequirementsAsync();
		var restricted = requirements.HasPageRules(source.Id) || requirements.Required(source, WikiAction.Delete).Count > 0;
		var editor = await names.NameOfAsync(selected.LastEditorDbref, ct);
		var available = new[] { prepared.SourceLocale }.Concat(prepared.Candidates
			.Where(candidate => candidate.Published || prepared.IncludeDrafts).Select(candidate => candidate.Locale))
			.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		var dto = new WikiPageDto(source.Id, source.Slug, selected.Title, source.Namespace, selected.MarkdownSource,
			selected.RenderedHtml, selected.PlainText, source.CreatedAt, selected.UpdatedAt, restricted,
			selected.RevisionNumber, source.Categories, selected.Published)
		{
			Locale = selected.Locale,
			RequestedLocale = selected.RequestedLocale,
			IsFallback = selected.IsFallback,
			AvailableLocales = available,
			LastEditedBy = editor,
			Image = WikiImages.FirstImageUrl(selected.RenderedHtml)
		};
		if (await FinalizeAsync(user, prepared, ct) is not WikiAccess current) return new NotFound();
		return dto with { Access = new WikiAccessDto(current.Read, current.Edit, current.Delete, current.Manage) };
	}

	private async Task<Found<Prepared>> PrepareAsync(ClaimsPrincipal user, Target target, string? lang, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var reader = await readers.ResolveAsync(user, ct);
		if (await CurrentSourceAsync(target, ct) is not WikiPage page || !await access.CanSeeAsync(reader, page))
			return new NotFound();
		// Own categories by value; record equality alone would compare the list object.
		var source = page with { Categories = page.Categories.ToArray() };
		var sourceLocale = localization.SourceLocaleOf(source);
		var defaultLocale = localization.DefaultLocale;
		var requested = locales.NormalizeRequested(lang);
		if (sourceLocale.Length == 0 || defaultLocale != locales.DefaultLocale
			|| await CandidatesAsync(source.Id, ct) is not Candidate[] candidates)
			return new NotFound();
		var drafts = reader.Has(PortalPermission.WikiDrafts);
		var selected = await localization.LocalizeAsync(source, lang, drafts);
		var prepared = new Prepared(target, source, sourceLocale, defaultLocale, requested, lang, drafts, candidates, selected);
		return await SelectionMatchesAsync(prepared, ct) ? prepared : new NotFound();
	}

	private async Task<Found<WikiAccess>> FinalizeAsync(ClaimsPrincipal user, Prepared prepared, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var reader = await readers.ResolveAsync(user, ct);
		if (reader.Has(PortalPermission.WikiDrafts) != prepared.IncludeDrafts
			|| await CurrentSourceAsync(prepared.Target, ct) is not WikiPage current
			|| !SameSource(prepared.Source, current)
			|| localization.DefaultLocale != prepared.DefaultLocale || locales.DefaultLocale != prepared.DefaultLocale
			|| localization.SourceLocaleOf(current) != prepared.SourceLocale
			|| locales.NormalizeRequested(prepared.RawRequest) != prepared.RequestedLocale
			|| await CandidatesAsync(current.Id, ct) is not Candidate[] candidates
			|| !candidates.SequenceEqual(prepared.Candidates)
			|| !await SelectionMatchesAsync(prepared, ct))
			return new NotFound();
		var permissions = await access.ForPageAsync(reader, current);
		ct.ThrowIfCancellationRequested();
		// Last awaited operation: no localization, payload decoration or lookup follows this decision.
		if (!await access.CanSeeAsync(reader, current)) return new NotFound();
		return permissions;
	}

	private async Task<Found<WikiPage>> CurrentSourceAsync(Target target, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (!Enum.TryParse<WikiNamespace>(target.Namespace, true, out var ns)
			|| WikiHelpers.NamespaceName(ns) != target.Namespace)
			return new NotFound();
		if (target.PackageId is { } packageId)
		{
			if (await packages.GetInstalledPackageAsync(packageId) is not InstalledPackageRecord installed
				|| installed.Id != packageId
				|| installed.WikiLandingPage != new PackageWikiLandingPageBinding(target.Namespace, target.Slug, target.PageId)
				|| await packages.GetPackageWikiPageAsync(packageId, target.Namespace, target.Slug) is not PackageWikiPageRecord owner
				|| !Owns(owner, target))
				return new NotFound();
			var owners = (await packages.GetAllPackageWikiPagesAsync()).Where(row => row.PageId == target.PageId).ToArray();
			if (owners is not [var only] || !Owns(only, target)) return new NotFound();
		}
		if (await wiki.GetByIdAsync(target.PageId) is not WikiPage page
			|| page.Id != target.PageId || page.Namespace != target.Namespace || page.Slug != target.Slug
			|| await wiki.GetBySlugAsync(target.Slug, ns) is not WikiPage routed || !SameSource(page, routed))
			return new NotFound();
		return page;
	}

	private static bool Owns(PackageWikiPageRecord owner, Target target)
		=> owner.PackageId == target.PackageId && owner.PageId == target.PageId
			&& owner.Namespace == target.Namespace && owner.Slug == target.Slug;

	private async Task<Found<Candidate[]>> CandidatesAsync(string pageId, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var result = new List<Candidate>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in await wiki.GetTranslationsAsync(pageId))
		{
			var locale = WikiHelpers.NormalizeLocaleOrEmpty(row.Locale);
			if (locale.Length == 0 || !seen.Add(locale)) return new NotFound();
			result.Add(new(locale, row.Title, row.Published, row.RevisionNumber, row.UpdatedAt));
		}
		return result.OrderBy(row => row.Locale, StringComparer.Ordinal).ToArray();
	}

	private async Task<bool> SelectionMatchesAsync(Prepared prepared, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var selected = prepared.Selection;
		if (!SameSource(selected.Page, prepared.Source) || selected.RequestedLocale != prepared.RequestedLocale)
			return false;
		var resolution = locales.Resolve(prepared.RequestedLocale, prepared.SourceLocale,
			prepared.Candidates.Where(row => row.Published || prepared.IncludeDrafts).Select(row => row.Locale).ToArray());
		if (resolution.Locale != selected.Locale || resolution.IsFallback != selected.IsFallback) return false;
		if (selected.Locale == prepared.SourceLocale)
		{
			var source = prepared.Source;
			return selected.Title == source.Title && selected.Published == source.Published
				&& selected.RevisionNumber == source.RevisionNumber && selected.UpdatedAt == source.UpdatedAt
				&& selected.LastEditorDbref == source.LastEditorDbref && selected.MarkdownSource == source.MarkdownSource
				&& selected.RenderedHtml == source.RenderedHtml && selected.PlainText == source.PlainText;
		}
		var candidate = prepared.Candidates.SingleOrDefault(row => row.Locale == selected.Locale);
		if (candidate is null || candidate.Title != selected.Title || candidate.Published != selected.Published
			|| candidate.Revision != selected.RevisionNumber || candidate.UpdatedAt != selected.UpdatedAt
			|| !(candidate.Published || prepared.IncludeDrafts)
			|| await wiki.GetTranslationAsync(prepared.Source.Id, selected.Locale) is not WikiTranslation served)
			return false;
		return served.PageId == prepared.Source.Id && served.Locale == selected.Locale && served.Title == selected.Title
			&& served.Published == selected.Published && (served.Published || prepared.IncludeDrafts)
			&& served.RevisionNumber == selected.RevisionNumber && served.UpdatedAt == selected.UpdatedAt
			&& served.LastEditorDbref == selected.LastEditorDbref && served.MarkdownSource == selected.MarkdownSource
			&& served.RenderedHtml == selected.RenderedHtml && served.PlainText == selected.PlainText;
	}

	private static bool SameSource(WikiPage left, WikiPage right)
		=> left.Id == right.Id && left.Namespace == right.Namespace && left.Slug == right.Slug
			&& left.SourceLocale == right.SourceLocale && left.Title == right.Title && left.Published == right.Published
			&& left.RevisionNumber == right.RevisionNumber && left.UpdatedAt == right.UpdatedAt && left.CreatedAt == right.CreatedAt
			&& left.AuthorDbref == right.AuthorDbref && left.LastEditorDbref == right.LastEditorDbref
			&& left.Categories.SequenceEqual(right.Categories, StringComparer.Ordinal)
			&& left.MarkdownSource == right.MarkdownSource && left.RenderedHtml == right.RenderedHtml && left.PlainText == right.PlainText;
}
