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
}
