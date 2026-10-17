using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Database.Lightning;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>Current storage boundaries, not a semantic freshness or atomic-index guarantee.</summary>
public class SemanticMutationCoverageTests : LightningDatabaseFixture
{
	private enum Impact { Read, PresentationOnly, SemanticMutation }

	// Explicit names intentionally make interface expansion demand a classification review.
	private static readonly Dictionary<string, Impact> WikiMethods = new()
	{
		["GetPageBySlugAsync"] = Impact.Read,
		["GetPageByIdAsync"] = Impact.Read,
		["GetRecentPagesAsync"] = Impact.Read,
		["GetPagesAsync"] = Impact.Read,
		["CountPagesAsync"] = Impact.Read,
		["CountPagesByStateAsync"] = Impact.Read,
		["CountPagesByCategoryAsync"] = Impact.Read,
		["GetPagesByCategoryAsync"] = Impact.Read,
		["GetRequirementsAsync"] = Impact.Read,
		["GetPinnedCategoriesAsync"] = Impact.Read,
		["GetRevisionsAsync"] = Impact.Read,
		["GetRevisionsBeforeAsync"] = Impact.Read,
		["GetRevisionAsync"] = Impact.Read,
		["GetTranslationSummariesAsync"] = Impact.Read,
		["GetTranslationsAsync"] = Impact.Read,
		["GetTranslationAsync"] = Impact.Read,
		["CreatePageAsync"] = Impact.SemanticMutation,
		["UpdatePageBodyAsync"] = Impact.SemanticMutation,
		["DeletePageAsync"] = Impact.SemanticMutation,
		["SetRequirementsAsync"] = Impact.SemanticMutation,
		["SetPageTitleAsync"] = Impact.SemanticMutation,
		["SetPageMetadataAsync"] = Impact.SemanticMutation,
		["SetCategoryPinnedAsync"] = Impact.PresentationOnly,
		["WriteTranslationAsync"] = Impact.SemanticMutation,
		["DeleteTranslationAsync"] = Impact.SemanticMutation
	};

	[Test]
	public async Task EveryWikiStoreMethodHasAnExplicitImpact()
		=> await Assert.That(typeof(IWikiStore).GetMethods().Select(m => m.Name)).IsEquivalentTo(WikiMethods.Keys);

	[Test]
	public async Task PackageOwnershipSurfaceIsExplicit()
	{
		var names = typeof(IPackageRegistryService).GetMethods()
			.Where(m => m.Name.Contains("Wiki", StringComparison.Ordinal) || m.Name == "RemoveInstalledPackageAsync")
			.Select(m => m.Name);
		await Assert.That(names).IsEquivalentTo(new[]
		{
			"UpsertPackageWikiPageAsync", "GetPackageWikiPageAsync", "GetPackageWikiPagesAsync",
			"GetAllPackageWikiPagesAsync", "RemovePackageWikiPageAsync", "RemoveInstalledPackageAsync"
		});
	}

	private async Task<WikiPage> AddAsync(string slug)
	{
		var at = DateTimeOffset.UnixEpoch;
		return (await ((IWikiStore)Db).CreatePageAsync(new WikiPage("", slug, slug, "main", "v1", "v1", "v1", "#1", "#1", at, at, 1))).Expect<WikiPage>();
	}

	[Test]
	public async Task RequirementChangeDoesNotAdvanceSourceRevision()
	{
		var page = await AddAsync("requirements");
		var set = new WikiRequirementSet(WikiRuleTarget.ForPage(page.Id),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["lore.read"] }, "#1", DateTimeOffset.UnixEpoch);
		(await ((IWikiStore)Db).SetRequirementsAsync(set)).Expect<None>();
		await ReopenAsync();
		await Assert.That((await ((IWikiStore)Db).GetPageByIdAsync(page.Id)).Expect<WikiPage>().RevisionNumber).IsEqualTo(1);
		await Assert.That((await ((IWikiStore)Db).GetRequirementsAsync()).Single(s => s.Target == set.Target).Required[WikiAction.Read])
			.IsEquivalentTo(new[] { "lore.read" });
	}

	[Test]
	public async Task OwnershipChangesMembershipWithoutAdvancingTheBody()
	{
		var page = await AddAsync("owned");
		IPackageRegistryService registry = Db;
		await registry.UpsertPackageWikiPageAsync(new PackageWikiPageRecord("rules", page.Id, page.Namespace,
			page.Slug, page.Title, page.MarkdownSource, [], true, PackageWikiRequires.None, false, "1.0.0"));
		await ReopenAsync();
		registry = Db;
		await Assert.That((await registry.GetPackageWikiPagesAsync("rules")).Single().PageId).IsEqualTo(page.Id);
		await Assert.That((await ((IWikiStore)Db).GetPageByIdAsync(page.Id)).Expect<WikiPage>().RevisionNumber).IsEqualTo(1);
		await registry.RemovePackageWikiPageAsync("rules", "main", "owned");
		await Assert.That(await registry.GetPackageWikiPagesAsync("rules")).IsEmpty();
		await Assert.That((await ((IWikiStore)Db).GetPageByIdAsync(page.Id)).Expect<WikiPage>().RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task RemovingAPackageDropsOwnershipButLeavesThePage()
	{
		var page = await AddAsync("released");
		IPackageRegistryService registry = Db;
		await registry.UpsertPackageWikiPageAsync(new PackageWikiPageRecord("rules", page.Id, page.Namespace,
			page.Slug, page.Title, page.MarkdownSource, [], true, PackageWikiRequires.None, false, "1.0.0"));
		await registry.RemoveInstalledPackageAsync("rules");
		await Assert.That(await registry.GetPackageWikiPagesAsync("rules")).IsEmpty();
		await Assert.That((await ((IWikiStore)Db).GetPageByIdAsync(page.Id)).Expect<WikiPage>().RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task RecreatingASlugAllocatesANewIdentity()
	{
		var original = await AddAsync("recreated");
		(await ((IWikiStore)Db).DeletePageAsync(original.Id)).Expect<None>();
		var replacement = await AddAsync("recreated");
		await Assert.That(replacement.Id).IsNotEqualTo(original.Id);
		await Assert.That((await ((IWikiStore)Db).GetPageByIdAsync(original.Id)) is NotFound).IsTrue();
		await Assert.That((await ((IWikiStore)Db).GetPageBySlugAsync("main", "recreated")).Expect<WikiPage>().Id).IsEqualTo(replacement.Id);
	}
}
