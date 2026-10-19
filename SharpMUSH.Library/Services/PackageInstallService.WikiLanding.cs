using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Queries.Database.Wiki;

namespace SharpMUSH.Library.Services;

/// <summary>Authored wiki entrance validation and exact installed identity selection.</summary>
public partial class PackageInstallService
{
	private record struct PackageWikiLandingAbsent;
	private sealed record PackageWikiLandingBound(PackageWikiLandingPageBinding Binding);
	private union PackageWikiLandingSelection(PackageWikiLandingAbsent, PackageWikiLandingBound);

	private static Error<string> InvalidWikiLanding(IReadOnlyList<PackageManifestIssue> issues)
		=> new($"Plan is blocked: {string.Join("; ", issues.Select(issue => $"{issue.Path}: {issue.Message}"))}");

	private static bool SameWikiLandingTarget(PackageWikiLandingPageBinding binding, PackageWikiLandingPageSpec target)
		=> binding.Namespace == WikiHelpers.NamespaceName(target.Namespace) && binding.Slug == target.Page;

	private static string WikiLandingReplacementMessage(PackageWikiLandingPageBinding previous)
		=> $"Wiki landing page {previous.Namespace}:{previous.Slug} no longer identifies the previously bound page. "
			+ "Clear wiki_landing_page in a successful apply before explicitly re-adding it, or choose a different declared target; "
			+ "the same authored target cannot silently bind a replacement page.";

	/// <summary>
	/// Refuses a predictable replacement before writes. Missing ownership alone may be repaired by an explicit
	/// planner adoption of the same live ID; the post-apply resolver must corroborate that ownership again.
	/// </summary>
	private async Task<IReadOnlyList<PackageManifestIssue>> CheckPreviousWikiLandingAsync(
		PackageManifest manifest, InstalledPackageRecord? installed, CancellationToken ct)
	{
		if (manifest.WikiLandingPage is not { } target || installed?.WikiLandingPage is not { } previous
			|| !SameWikiLandingTarget(previous, target)) return [];

		ct.ThrowIfCancellationRequested();
		var owner = await registry.GetPackageWikiPageAsync(manifest.Name, previous.Namespace, previous.Slug);
		if (owner is PackageWikiPageRecord record && (record.PackageId != manifest.Name
			|| record.Namespace != previous.Namespace || record.Slug != previous.Slug || record.PageId != previous.PageId)
			|| await mediator.Send(new GetWikiPageByIdQuery(previous.PageId), ct) is not WikiPage page
			|| page.Id != previous.PageId || page.Namespace != previous.Namespace || page.Slug != previous.Slug)
		{
			return [PackageManifestIssue.Error("wiki_landing_page", WikiLandingReplacementMessage(previous))];
		}

		return [];
	}

	/// <summary>Resolves only the page actually owned and live after wiki apply; never searches by slug for a substitute.</summary>
	private async Task<Result<PackageWikiLandingSelection>> ResolveWikiLandingAsync(
		PackageManifest manifest, InstalledPackageRecord? installed, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (manifest.WikiLandingPage is not { } target)
		{
			PackageWikiLandingSelection absent = new PackageWikiLandingAbsent();
			return absent;
		}

		var ns = WikiHelpers.NamespaceName(target.Namespace);
		if (await registry.GetPackageWikiPageAsync(manifest.Name, ns, target.Page) is not PackageWikiPageRecord owner
			|| owner.PackageId != manifest.Name || owner.Namespace != ns || owner.Slug != target.Page
			|| await mediator.Send(new GetWikiPageByIdQuery(owner.PageId), ct) is not WikiPage page
			|| page.Id != owner.PageId || page.Namespace != ns || page.Slug != target.Page)
		{
			return new Error<string>($"wiki_landing_page: {target.Label} must be owned by this package and live after wiki apply. "
				+ "Resolve its content/ownership conflict, or clear/change the declared landing target.");
		}

		if (installed?.WikiLandingPage is { } previous && SameWikiLandingTarget(previous, target)
			&& previous.PageId != owner.PageId)
		{
			return new Error<string>(WikiLandingReplacementMessage(previous));
		}

		PackageWikiLandingSelection bound = new PackageWikiLandingBound(new PackageWikiLandingPageBinding(ns, target.Page, owner.PageId));
		return bound;
	}

	private static IReadOnlyList<string> WikiLandingNotes(PackageManifest manifest, InstalledPackageRecord? installed)
	{
		var previous = installed?.WikiLandingPage;
		if (manifest.WikiLandingPage is not { } target)
		{
			return previous is null ? [] : [$"Wiki landing page: remove {previous.Namespace}:{previous.Slug}."];
		}

		if (previous is null) return [$"Wiki landing page: add {target.Label}."];
		return SameWikiLandingTarget(previous, target)
			? []
			: [$"Wiki landing page: {previous.Namespace}:{previous.Slug} -> {target.Label}."];
	}

	/// <summary>Checks a captured binding exactly, without re-adoption, content writes or replacement lookup.</summary>
	private async Task<bool> IsWikiLandingLiveAsync(string packageId, PackageWikiLandingPageBinding binding, CancellationToken ct)
		=> await registry.GetPackageWikiPageAsync(packageId, binding.Namespace, binding.Slug) is PackageWikiPageRecord owner
			&& owner.PackageId == packageId && owner.Namespace == binding.Namespace && owner.Slug == binding.Slug
			&& owner.PageId == binding.PageId
			&& await mediator.Send(new GetWikiPageByIdQuery(binding.PageId), ct) is WikiPage page
			&& page.Id == binding.PageId && page.Namespace == binding.Namespace && page.Slug == binding.Slug;

	private async Task<PackageWikiLandingSelection> RestoreWikiLandingAsync(
		string packageId, PackageRevisionSnapshot snapshot, List<string> notes, CancellationToken ct)
	{
		if (snapshot.WikiLandingPage is not { } binding) return new PackageWikiLandingAbsent();
		if (await IsWikiLandingLiveAsync(packageId, binding, ct)) return new PackageWikiLandingBound(binding);
		notes.Add($"Wiki landing page {binding.Namespace}:{binding.Slug} was withheld: the captured page is no longer "
			+ "owned and live with its exact page identity. Wiki content and ownership were left unchanged.");
		return new PackageWikiLandingAbsent();
	}
}
