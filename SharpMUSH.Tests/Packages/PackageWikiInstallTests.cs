using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A content pack's <c>wiki:</c> pages (format 1.4), planned, installed, upgraded and uninstalled against the
/// real world. Every test names its own package, pages and categories, so they share the wiki without meeting.
/// </summary>
public class PackageWikiInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => Services.GetRequiredService<IWikiService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Services.GetRequiredService<ISharpDatabase>();

	private static string FreshId() => "wk" + Guid.NewGuid().ToString("N")[..10];

	private static PackageManifest Manifest(string id, string version, string wiki, string extra = "")
		=> new PackageManifestService().ParseManifest($$"""
			format: 1.4
			package: {{id}}
			version: "{{version}}"
			{{extra}}
			wiki:
			{{wiki}}
			""").Expect<ParsedPackageManifest>().Manifest;

	/// <summary>A main page <c>&lt;id&gt;_road</c> in the category <c>&lt;id&gt;</c>, with <paramref name="body"/>.</summary>
	private static string Road(string id, string body, string title = "Coast Road")
		=> $"""
			  - page: main:{id}_road
			    title: {title}
			    categories: [{id}]
			    markdown: |
			{Indent(body, "      ")}
			""";

	/// <summary>The category page of <c>&lt;id&gt;</c>, pinned to the wiki home.</summary>
	private static string Category(string id)
		=> $"""
			  - page: category:{id}
			    title: Travel {id}
			    pin: true
			    markdown: Roads and ferries.
			""";

	private static string Indent(string text, string indent) => string.Join('\n', text.Split('\n').Select(line => indent + line));

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, params PackageConflictDecision[] decisions)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{manifest.Name}/", "commit-1", "main"),
			new Dictionary<string, string>(), decisions));

	private async Task<WikiPage?> PageAsync(string slug, WikiNamespace ns = WikiNamespace.Main)
		=> await Wiki.GetBySlugAsync(slug, ns) is WikiPage page ? page : null;

	private const string V1 = "# Coast Road\nThe road runs north.\nOne inn halfway.\nThe lighthouse at the end.";

	[Test]
	public async Task InstallCreatesThePagesAndUninstallDeletesThemUntouched()
	{
		var id = FreshId();
		var manifest = Manifest(id, "1.0.0", Road(id, V1) + "\n" + Category(id));

		var plan = await Installer.PlanAsync(manifest);
		await Assert.That(plan.Wiki!.Select(w => w.Action).ToArray())
			.IsEquivalentTo(new[] { PackageWikiAction.Create, PackageWikiAction.Create });
		await Assert.That(plan.Objects).IsEmpty();

		var applied = (await ApplyAsync(manifest)).Expect<PackageApplyResult>();
		await Assert.That(applied.Notes).Contains($"Created wiki page {id}_road.");

		var road = (await PageAsync($"{id}_road"))!;
		await Assert.That(road.Title).IsEqualTo("Coast Road");
		await Assert.That(road.MarkdownSource).IsEqualTo(V1 + "\n");
		await Assert.That(road.Categories).IsEquivalentTo(new[] { id });
		await Assert.That(road.AuthorDbref).StartsWith("#7");
		await Assert.That((await Wiki.GetRevisionsAsync(road.Id)).Single().EditSummary).IsEqualTo($"{id} 1.0.0");
		await Assert.That(await Wiki.GetPinnedCategoriesAsync()).Contains(id);

		var owned = await Registry.GetPackageWikiPagesAsync(id);
		await Assert.That(owned.Count).IsEqualTo(2);
		await Assert.That(owned.Single(o => o.Slug == $"{id}_road").PageId).IsEqualTo(road.Id);

		// Planned again, nothing changes.
		await Assert.That((await Installer.PlanAsync(manifest)).Wiki!.All(w => w.Action == PackageWikiAction.NoChange)).IsTrue();

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That(await PageAsync($"{id}_road")).IsNull();
		await Assert.That(await PageAsync(id, WikiNamespace.Category)).IsNull();
		await Assert.That(await Wiki.GetPinnedCategoriesAsync()).DoesNotContain(id);
		await Assert.That((await Registry.GetPackageWikiPagesAsync(id)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task AnUpgradeMergesStaffEditsAndUninstallKeepsATouchedPage()
	{
		var id = FreshId();
		(await ApplyAsync(Manifest(id, "1.0.0", Road(id, V1) + "\n" + Category(id)))).Expect<PackageApplyResult>();
		var road = (await PageAsync($"{id}_road"))!;

		// Staff edit the second line and file the page in a category of their own.
		var staffBody = V1.Replace("The road runs north.", "The road runs north, past the cliffs.") + "\n";
		(await Wiki.UpdateAsync(road.Id, staffBody, "#1", "staff edit")).Expect<WikiPage>();
		(await Wiki.SetMetadataAsync(road.Id, [id, $"{id}_lore"], published: true)).Expect<WikiPage>();

		// The new version changes the last line and the title.
		var v2Body = V1.Replace("The lighthouse at the end.", "The lighthouse at the end, still lit.");
		var v2 = Manifest(id, "1.1.0", Road(id, v2Body, title: "The Coast Road") + "\n" + Category(id));

		var plan = await Installer.PlanAsync(v2);
		var change = plan.Wiki!.Single(w => w.Page == $"{id}_road");
		await Assert.That(change.Action).IsEqualTo(PackageWikiAction.Update);
		await Assert.That(plan.HasConflicts).IsFalse();

		(await ApplyAsync(v2)).Expect<PackageApplyResult>();
		var merged = (await PageAsync($"{id}_road"))!;
		await Assert.That(merged.Id).IsEqualTo(road.Id);
		await Assert.That(merged.Title).IsEqualTo("The Coast Road");
		await Assert.That(merged.MarkdownSource).IsEqualTo(
			"# Coast Road\nThe road runs north, past the cliffs.\nOne inn halfway.\nThe lighthouse at the end, still lit.\n");
		await Assert.That(merged.Categories).IsEquivalentTo(new[] { id, $"{id}_lore" });
		await Assert.That((await Wiki.GetRevisionsAsync(merged.Id)).First().EditSummary).IsEqualTo($"{id} 1.1.0, merged");
		await Assert.That((await Registry.GetPackageWikiPageAsync(id, "main", $"{id}_road")).Expect<PackageWikiPageRecord>().Markdown)
			.IsEqualTo(v2Body + "\n");

		// The road was changed by staff and stays; the category page nobody touched goes.
		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That((await PageAsync($"{id}_road"))!.Id).IsEqualTo(road.Id);
		await Assert.That(await PageAsync(id, WikiNamespace.Category)).IsNull();
		await Assert.That((await Registry.GetPackageWikiPagesAsync(id)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task AClashNeedsADecisionAndKeepMineKeepsTheGamesLines()
	{
		var id = FreshId();
		(await ApplyAsync(Manifest(id, "1.0.0", Road(id, V1)))).Expect<PackageApplyResult>();
		var road = (await PageAsync($"{id}_road"))!;
		(await Wiki.UpdateAsync(road.Id, V1.Replace("One inn halfway.", "Two inns, by staff.") + "\n", "#1")).Expect<WikiPage>();

		var v2 = Manifest(id, "1.1.0", Road(id, V1.Replace("One inn halfway.", "One inn, by the package.")
			.Replace("# Coast Road", "# The Coast Road")));
		var change = (await Installer.PlanAsync(v2)).Wiki!.Single();
		await Assert.That(change.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
		await Assert.That(change.MergedMarkdown!).Contains("<<<<<<< game");

		var refused = (await ApplyAsync(v2)).Expect<Error<string>>();
		await Assert.That(refused.Value).Contains("Unresolved conflicts");
		await Assert.That((await PageAsync($"{id}_road"))!.MarkdownSource).Contains("Two inns, by staff.");

		(await ApplyAsync(v2, new PackageConflictDecision(change.TargetRef, change.Page, PackageConflictResolution.KeepMine)))
			.Expect<PackageApplyResult>();
		await Assert.That((await PageAsync($"{id}_road"))!.MarkdownSource).IsEqualTo(
			"# The Coast Road\nThe road runs north.\nTwo inns, by staff.\nThe lighthouse at the end.\n");

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test]
	public async Task APageTheGameAlreadyHasIsAConflictAndKeepMineLeavesItUnowned()
	{
		var id = FreshId();
		var existing = (await Wiki.CreateAsync("Coast Road", "The game's own page.", "#1", slug: $"{id}_road")).Expect<WikiPage>();
		var manifest = Manifest(id, "1.0.0", Road(id, V1));

		var change = (await Installer.PlanAsync(manifest)).Wiki!.Single();
		await Assert.That(change.Conflict).IsEqualTo(PackageConflictKind.AddAdd);

		(await ApplyAsync(manifest, new PackageConflictDecision(change.TargetRef, change.Page, PackageConflictResolution.KeepMine)))
			.Expect<PackageApplyResult>();
		await Assert.That((await PageAsync($"{id}_road"))!.MarkdownSource).IsEqualTo("The game's own page.");
		await Assert.That((await Registry.GetPackageWikiPagesAsync(id)).Count).IsEqualTo(0);

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That((await PageAsync($"{id}_road"))!.Id).IsEqualTo(existing.Id);
	}

	[Test]
	public async Task APageAnotherPackageOwnsBlocksThePlan()
	{
		var id = FreshId();
		var rival = FreshId();
		(await ApplyAsync(Manifest(id, "1.0.0", Road(id, V1)))).Expect<PackageApplyResult>();

		var other = Manifest(rival, "1.0.0", Road(id, V1));
		var plan = await Installer.PlanAsync(other);
		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(plan.Wiki!.Single().Detail!).Contains(id);

		var refused = (await ApplyAsync(other)).Expect<Error<string>>();
		await Assert.That(refused.Value).Contains("Plan is blocked");
		await Assert.That((await Registry.GetInstalledPackageAsync(rival)).Value).IsTypeOf<NotFound>();

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test]
	public async Task APageTheVersionDropsGoesAndRequirementsAreWritten()
	{
		var id = FreshId();
		var permission = $"softcode.{id}.gm";
		var declare = $$"""
			categories:
			  permissions:
			    - name: Coast {{id}}
			      description: The coast pack.
			permissions:
			  - name: {{permission}}
			    category: Coast {{id}}
			""";
		var secret = $"""
			  - page: main:{id}_secret
			    title: Secret
			    requires:
			      view: [{permission}]
			      edit: [wiki.admin]
			    markdown: Hidden.
			""";
		(await ApplyAsync(Manifest(id, "1.0.0", Road(id, V1) + "\n" + secret, declare))).Expect<PackageApplyResult>();

		var page = (await PageAsync($"{id}_secret"))!;
		var set = (await Wiki.GetRequirementsAsync()).For(WikiRuleTarget.ForPage(page.Id))!;
		await Assert.That(set.For(WikiAction.Read)).IsEquivalentTo(new[] { permission });
		await Assert.That(set.For(WikiAction.Edit)).IsEquivalentTo(new[] { "wiki.admin" });

		var v2 = Manifest(id, "1.1.0", Road(id, V1), declare);
		var plan = await Installer.PlanAsync(v2);
		await Assert.That(plan.Wiki!.Single(w => w.Page == $"{id}_secret").Action).IsEqualTo(PackageWikiAction.Delete);

		var applied = (await ApplyAsync(v2)).Expect<PackageApplyResult>();
		await Assert.That(applied.Notes).Contains("Deleted wiki page Secret: nobody had changed it.");
		await Assert.That(await PageAsync($"{id}_secret")).IsNull();
		await Assert.That((await Registry.GetPackageWikiPagesAsync(id)).Select(p => p.Slug).ToArray())
			.IsEquivalentTo(new[] { $"{id}_road" });

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test]
	public async Task FileBodiesComeFromThePackagesFiles()
	{
		var id = FreshId();
		var dir = Directory.CreateTempSubdirectory("wiki-install-");
		try
		{
			Directory.CreateDirectory(Path.Combine(dir.FullName, "wiki"));
			await File.WriteAllTextAsync(Path.Combine(dir.FullName, "wiki", "road.md"), "# From a file\r\n");
			var manifest = Manifest(id, "1.0.0", $"""
				  - page: main:{id}_road
				    title: From a File
				    file: wiki/road.md
				""");

			var unread = (await Installer.PlanAsync(manifest)).Wiki!.Single();
			await Assert.That(unread.Action).IsEqualTo(PackageWikiAction.Blocked);

			var request = new PackageApplyRequest(
				new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", "commit-1", "main"),
				new Dictionary<string, string>(), []);
			(await Installer.ApplyAsync(manifest, request, fileSource: new DirectoryPackageFileSource(dir.FullName)))
				.Expect<PackageApplyResult>();
			await Assert.That((await PageAsync($"{id}_road"))!.MarkdownSource).IsEqualTo("# From a file\n");

			await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
			await Assert.That(await PageAsync($"{id}_road")).IsNull();
		}
		finally
		{
			dir.Delete(recursive: true);
		}
	}
}
