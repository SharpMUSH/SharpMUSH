using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Authentication;

namespace SharpMUSH.Tests.Server.Controllers;

public class WikiCollectionsControllerTests
{
	private static (WikiCollectionsController Controller, WikiStoreService Wiki, IPackageRegistryService Packages) Build()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var localization = new WikiLocalizationService(wiki, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);
		var access = new WikiAccessService(wiki, InMemoryRoleRegistry.Seeded(), new PermissionResolver());
		var packages = Substitute.For<IPackageRegistryService>();
		packages.GetInstalledPackagesAsync().Returns(Task.FromResult<IReadOnlyList<InstalledPackageRecord>>([]));
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>([]));
		var controller = new WikiCollectionsController(wiki, localization, access, Substitute.For<IWikiNameResolver>(), packages,
			NullLogger<WikiCollectionsController>.Instance)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		return (controller, wiki, packages);
	}

	private static T Value<T>(IActionResult response) => response is OkObjectResult { Value: T value }
		? value : throw new InvalidOperationException($"Expected successful {typeof(T).Name}, received {response.GetType().Name}.");

	private static PackageWikiPageRecord Own(WikiPage page, string package = "pf2e-remaster") => new(package, page.Id,
		page.Namespace, page.Slug, page.Title, page.MarkdownSource, page.Categories, page.Published, PackageWikiRequires.None, false, "1.1.1");

	// JSON seeds the optional registry field without requiring its new compile-time API during RED.
	private static InstalledPackageRecord Installed(string id, string? label)
	{
		var json = JsonSerializer.SerializeToNode(new InstalledPackageRecord(id, "1.0.0", "https://example.invalid/packages", null,
			"commit", null, DateTimeOffset.UnixEpoch, 1)) ?? throw new InvalidOperationException("The registry fixture could not serialize.");
		json["WikiCollectionName"] = label;
		return json.Deserialize<InstalledPackageRecord>() ?? throw new InvalidOperationException("The registry fixture could not deserialize.");
	}

	[Test]
	[Arguments("Pathfinder 2e", "Pathfinder 2e")]
	[Arguments("Règles: deuxième édition", "Règles: deuxième édition")]
	[Arguments(null, "generic rules")]
	public async Task AuthoredRegistryLabelNamesNavigationAndResultRowsWhileCollectionIdentityStaysStable(string? label, string expected)
	{
		var (controller, wiki, packages) = Build();
		var page = (await wiki.CreateAsync("Guide", "Rules", "#1")).Expect<WikiPage>();
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>([Own(page, "generic-rules")]));
		packages.GetInstalledPackagesAsync().Returns(Task.FromResult<IReadOnlyList<InstalledPackageRecord>>([Installed("generic-rules", label)]));

		var home = Value<WikiNavigationDto>(await controller.Navigation());
		await Assert.That(home.Collections.Single().Id).IsEqualTo("package:generic-rules");
		await Assert.That(home.Collections.Single().Name).IsEqualTo(expected);
		var selected = Value<WikiNavigationDto>(await controller.Navigation("package:generic-rules"));
		await Assert.That(selected.Collection).IsEqualTo("package:generic-rules");
		await Assert.That(selected.Collections.Single().Name).IsEqualTo(expected);
		foreach (var collection in new[] { "all", "package:generic-rules" })
		{
			var rows = Value<List<WikiPageSummaryDto>>(await controller.Pages(collection));
			await Assert.That(rows.Single().CollectionId).IsEqualTo("package:generic-rules");
			await Assert.That(rows.Single().CollectionName).IsEqualTo(expected);
		}
	}

	[Test]
	public async Task InstalledDisplayNameDoesNotRevealAHiddenOnlyCollection()
	{
		var (controller, wiki, packages) = Build();
		var hidden = (await wiki.CreateAsync("Private guide", "Private", "#1")).Expect<WikiPage>();
		await wiki.SetMetadataAsync(hidden.Id, [], false);
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>([Own(hidden, "private-rules")]));
		packages.GetInstalledPackagesAsync().Returns(Task.FromResult<IReadOnlyList<InstalledPackageRecord>>([Installed("private-rules", "PRIVATE_AUTHORED_LABEL")]));
		var home = Value<WikiNavigationDto>(await controller.Navigation());
		await Assert.That(home.Collections).IsEmpty();
		await Assert.That(JsonSerializer.Serialize(home)).DoesNotContain("PRIVATE_AUTHORED_LABEL");
		await Assert.That(await controller.Navigation("package:private-rules") is NotFoundResult).IsTrue();
		await Assert.That(Value<List<WikiPageSummaryDto>>(await controller.Pages("all"))).IsEmpty();
	}

	[Test]
	public async Task SearchUsesVisibleTranslatedTitlesAndNeverReturnsPageBodies()
	{
		var (controller, wiki, packages) = Build();
		var page = (await wiki.CreateAsync("Blinded", "private body not in navigation", "#1", sourceLocale: "en")).Expect<WikiPage>();
		await wiki.UpsertTranslationAsync(page.Id, "fr", "Aveuglé", "texte", "#1", null, true, null);
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>([Own(page)]));
		var rows = Value<List<WikiPageSummaryDto>>(await controller.Pages("all", search: "aveuglé", lang: "fr"));
		await Assert.That(rows.Single().Title).IsEqualTo("Aveuglé");
		await Assert.That(rows.Single().CollectionId).IsEqualTo("package:pf2e-remaster");
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(rows)).DoesNotContain("private body not in navigation");
	}

	[Test]
	public async Task NavigationGroupsAll4711ImportedPagesWithoutMixingGameCategories()
	{
		var (controller, wiki, packages) = Build();
		var owned = new List<PackageWikiPageRecord>();
		for (var i = 0; i < 4711; i++)
			owned.Add(Own((await wiki.CreateAsync($"Rule {i:D4}", "ORC licensed rules", "#1", categories: [$"Rules {i % 130}"])).Expect<WikiPage>()));
		await wiki.CreateAsync("Game Setting", "setting", "#1", categories: ["Setting"]);
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>(owned));
		var home = Value<WikiNavigationDto>(await controller.Navigation());
		await Assert.That(home.Total).IsEqualTo(1);
		await Assert.That(home.Collections.Single().Pages).IsEqualTo(4711);
		await Assert.That(home.Categories.Select(c => c.Key)).IsEquivalentTo(new[] { "setting" });
		var selected = Value<WikiNavigationDto>(await controller.Navigation("package:pf2e-remaster"));
		await Assert.That(selected.Total).IsEqualTo(4711);
		await Assert.That(selected.Categories.Count).IsEqualTo(130);
		var result = (OkObjectResult)await controller.Pages("package:pf2e-remaster", skip: 4700, take: 20);
		await Assert.That(((List<WikiPageSummaryDto>)result.Value!).Count).IsEqualTo(11);
		await Assert.That(controller.Response.Headers["X-Total-Count"].ToString()).IsEqualTo("4711");
	}

	[Test]
	public async Task HiddenOnlyCollectionsAndCategoriesAreNeverDisclosed()
	{
		var (controller, wiki, packages) = Build();
		var hidden = (await wiki.CreateAsync("Unreleased rules", "secret", "#1", categories: ["Secret"])).Expect<WikiPage>();
		await wiki.SetMetadataAsync(hidden.Id, hidden.Categories, false);
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>([Own(hidden)]));
		var home = Value<WikiNavigationDto>(await controller.Navigation());
		await Assert.That(home.Collections).IsEmpty();
		await Assert.That(home.Categories).IsEmpty();
		await Assert.That(await controller.Navigation("package:pf2e-remaster") is NotFoundResult).IsTrue();
	}

	[Test]
	public async Task RecreatedPageDoesNotInheritOldOwnershipAndCollectionSearchIsScoped()
	{
		var (controller, wiki, packages) = Build();
		var original = (await wiki.CreateAsync("Blinded", "ORC", "#1", categories: ["Conditions"])).Expect<WikiPage>();
		await wiki.DeleteAsync(original.Id, "#1");
		await wiki.CreateAsync("Blinded", "game house rule", "#1", categories: ["Conditions"]);
		var imported = (await wiki.CreateAsync("Dazzled", "ORC", "#1", categories: ["Conditions"])).Expect<WikiPage>();
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>([Own(original), Own(imported)]));
		var home = Value<WikiNavigationDto>(await controller.Navigation());
		await Assert.That(home.Total).IsEqualTo(1);
		var rows = Value<List<WikiPageSummaryDto>>(await controller.Pages("package:pf2e-remaster", search: "Blinded"));
		await Assert.That(rows).IsEmpty();
		await Assert.That(controller.Response.Headers["X-Total-Count"].ToString()).IsEqualTo("0");
	}

	[Test]
	public async Task APageNamesThePagesEitherSideAndAnUnknownTokenReadsAsTheOffset()
	{
		var (controller, wiki, packages) = Build();
		var owned = new List<PackageWikiPageRecord>();
		for (var i = 0; i < 120; i++)
			owned.Add(Own((await wiki.CreateAsync($"Spell {i:D3}", "ORC", "#1", categories: ["Spells"])).Expect<WikiPage>()));
		packages.GetAllPackageWikiPagesAsync().Returns(Task.FromResult<IReadOnlyList<PackageWikiPageRecord>>(owned));
		async Task<(List<string> Titles, string Previous, string Next, string Total)> PageAsync(int skip = 0, string? after = null,
			string? before = null)
		{
			controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
			var rows = Value<List<WikiPageSummaryDto>>(await controller.Pages("package:pf2e-remaster", skip, 50, "Spells",
				after: after, before: before));
			var headers = controller.Response.Headers;
			return ([.. rows.Select(row => row.Title)], headers["X-Previous-Page"].ToString(), headers["X-Next-Page"].ToString(),
				headers["X-Total-Count"].ToString());
		}

		var first = await PageAsync();
		await Assert.That(first.Titles.First()).IsEqualTo("Spell 000");
		await Assert.That(first.Previous).IsEmpty();
		await Assert.That(first.Total).IsEqualTo("120");
		var second = await PageAsync(50, after: first.Next);
		await Assert.That(second.Titles.First()).IsEqualTo("Spell 050");
		var third = await PageAsync(100, after: second.Next);
		await Assert.That(third.Titles).Count().IsEqualTo(20);
		await Assert.That(third.Next).IsEmpty();
		var back = await PageAsync(50, before: third.Previous);
		await Assert.That(back.Titles).IsEquivalentTo(second.Titles);
		var forged = await PageAsync(50, after: "c1.forged");
		await Assert.That(forged.Titles).IsEquivalentTo(second.Titles);
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
		await Assert.That(Value<List<WikiPageSummaryDto>>(await controller.Pages("package:pf2e-remaster", 0, 0, "Spells"))).IsEmpty();
		await Assert.That(controller.Response.Headers["X-Total-Count"].ToString()).IsEqualTo("120");
	}
}
