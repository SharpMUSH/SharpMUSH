using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Packages;

public sealed class Pf2eCollectionWorld : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

/// <summary>Explicit full-pack acceptance probe; download the pinned pack before running, never from the test.</summary>
[Explicit]
public class Pf2eCollectionImportTests
{
	[ClassDataSource<Pf2eCollectionWorld>(Shared = SharedType.PerClass)]
	public required Pf2eCollectionWorld World { get; init; }

	[Test]
	public async Task FullImportAndRepeatApplyPreserveCollectionLinksAndLocalEdits()
	{
		var path = Environment.GetEnvironmentVariable("SHARPMUSH_PF2E_SOURCE")
			?? throw new InvalidOperationException("Set SHARPMUSH_PF2E_SOURCE to the pinned pf2e-remaster directory; see the wiki collection plan.");
		var manifest = new PackageManifestService().ParseManifest(await File.ReadAllTextAsync(Path.Join(path, "package.yaml")))
			.Expect<ParsedPackageManifest>().Manifest;
		await Assert.That(manifest.Name).IsEqualTo("pf2e-remaster");
		await Assert.That(manifest.Version.ToString()).IsEqualTo("1.1.1");
		await Assert.That(manifest.Wiki!.Count).IsEqualTo(4711);
		using var scope = World.Services.CreateScope();
		var services = scope.ServiceProvider;
		var installer = services.GetRequiredService<IPackageInstallService>();
		var wiki = services.GetRequiredService<IWikiService>();
		var request = new PackageApplyRequest(new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH.Packages", "pf2e-remaster/",
			"2659a733a4519b76e64a727be2687794afc04f6b", "main"), new Dictionary<string, string>(), []);
		(await installer.ApplyAsync(manifest, request, fileSource: new DirectoryPackageFileSource(path))).Expect<PackageApplyResult>();
		var registry = (IPackageRegistryService)services.GetRequiredService<ISharpDatabase>();
		await Assert.That((await registry.GetPackageWikiPagesAsync(manifest.Name)).Count).IsEqualTo(4711);
		var blinded = (await wiki.GetBySlugAsync("blinded", WikiNamespace.Main)).Expect<WikiPage>();
		var edited = blinded.MarkdownSource + "\nLocal campaign annotation.\n";
		(await wiki.UpdateAsync(blinded.Id, edited, "#1", "local annotation")).Expect<WikiPage>();
		(await installer.ApplyAsync(manifest, request, fileSource: new DirectoryPackageFileSource(path))).Expect<PackageApplyResult>();
		var kept = (await wiki.GetBySlugAsync("blinded", WikiNamespace.Main)).Expect<WikiPage>();
		await Assert.That(kept.Id).IsEqualTo(blinded.Id);
		await Assert.That(kept.MarkdownSource).IsEqualTo(edited);
		foreach (var slug in new[] { "ancestries", "alchemist_feats", "conditions", "character_creation" })
			await Assert.That(await wiki.GetBySlugAsync(slug, WikiNamespace.Category) is WikiPage).IsTrue();
		await wiki.CreateAsync("Collection Probe Setting", "Game-owned lore", "#1", categories: ["Campaign Setting"]);
		var controller = ActivatorUtilities.CreateInstance<WikiCollectionsController>(services);
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
		var home = Value<WikiNavigationDto>(await controller.Navigation());
		await Assert.That(home.Collections.Single(row => row.Id == "package:pf2e-remaster").Pages).IsEqualTo(4711);
		await Assert.That(home.Categories.Any(row => row.Key == "alchemist_feats")).IsFalse();
		var navigation = Value<WikiNavigationDto>(await controller.Navigation("package:pf2e-remaster"));
		await Assert.That(navigation.Total).IsEqualTo(4711);
		await Assert.That(navigation.Categories.Count).IsGreaterThanOrEqualTo(130);
		var last = Value<List<WikiPageSummaryDto>>(await controller.Pages("package:pf2e-remaster", skip: 4700));
		await Assert.That(last.Count).IsEqualTo(11);
		var search = Value<List<WikiPageSummaryDto>>(await controller.Pages("package:pf2e-remaster", search: "Blinded"));
		await Assert.That(search.Any(row => row.Slug == "blinded")).IsTrue();
		var all = new List<WikiPageSummaryDto>();
		for (var skip = 0; skip < navigation.Total; skip += 200)
			all.AddRange(Value<List<WikiPageSummaryDto>>(await controller.Pages("package:pf2e-remaster", skip, 200)));
		await Assert.That(all.Count).IsEqualTo(4711);
		if (Environment.GetEnvironmentVariable("SHARPMUSH_WIKI_CAPTURE_PATH") is { Length: > 0 } output)
		{
			Directory.CreateDirectory(output);
			var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
			await File.WriteAllTextAsync(Path.Join(output, "navigation.json"), JsonSerializer.Serialize(navigation, options));
			await File.WriteAllTextAsync(Path.Join(output, "pages.json"), JsonSerializer.Serialize(all, options));
			await File.WriteAllTextAsync(Path.Join(output, "home-navigation.json"), JsonSerializer.Serialize(home, options));
		}
	}

	private static T Value<T>(IActionResult result) => result is OkObjectResult { Value: T value }
		? value : throw new InvalidOperationException($"Expected successful {typeof(T).Name}: {result.GetType().Name}");
}
