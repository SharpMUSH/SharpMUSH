using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>Installed landing identity follows actual native package lifecycle without rewriting wiki history.</summary>
public class PackageWikiLandingPageLifecycleTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private sealed record ObservedBinding(string? Namespace, string? Slug, string? PageId);
	private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

	private static PackageManifest Manifest()
	{
		var id = "landing-life-" + Guid.NewGuid().ToString("N")[..12];
		return new PackageManifestService().ParseManifest($$"""
			format: 1.14
			package: {{id}}
			version: 1.0.0
			wiki_landing_page: main:{{id}}_entrance
			wiki:
			  - page: main:{{id}}_entrance
			    title: Lifecycle Entrance
			    markdown: Package source remains distinguishable.
			""").Expect<ParsedPackageManifest>().Manifest;
	}

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, params PackageConflictDecision[] decisions)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", manifest.Name + "/", "commit-" + manifest.Version, "main"),
			new Dictionary<string, string>(), decisions));

	private static ObservedBinding? Binding<T>(T value)
	{
		var json = JsonSerializer.SerializeToElement(value);
		if (!json.TryGetProperty("WikiLandingPage", out var binding) || binding.ValueKind == JsonValueKind.Null) return null;
		static string? Text(JsonElement json, string name)
			=> json.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
		return new(Text(binding, "Namespace"), Text(binding, "Slug"), Text(binding, "PageId"));
	}

	private async Task AssertPublishedBindingAsync(string id, ObservedBinding? expected)
	{
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(Binding(installed)).IsEqualTo(expected);
		await Assert.That(Binding((await Registry.GetInstalledPackagesAsync()).Single(package => package.Id == id))).IsEqualTo(expected);
		var revision = (await Registry.GetPackageRevisionAsync(id, installed.CurrentRevision)).Expect<PackageRevisionRecord>();
		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, SnapshotJson)
			?? throw new InvalidOperationException("Missing native revision snapshot.");
		await Assert.That(Binding(snapshot)).IsEqualTo(expected);
		if (expected is not null)
		{
			var owner = (await Registry.GetPackageWikiPageAsync(id, expected.Namespace!, expected.Slug!)).Expect<PackageWikiPageRecord>();
			var page = (await Wiki.GetByIdAsync(owner.PageId)).Expect<WikiPage>();
			await Assert.That(owner.PageId).IsEqualTo(expected.PageId);
			await Assert.That(page.Id).IsEqualTo(expected.PageId);
			await Assert.That(page.Namespace).IsEqualTo(expected.Namespace);
			await Assert.That(page.Slug).IsEqualTo(expected.Slug);
		}
	}

	private async Task<string> WikiStateAsync(string id, params PackageWikiPageSpec[] targets)
	{
		var pages = new List<WikiPage?>();
		foreach (var target in targets)
			pages.Add(await Wiki.GetBySlugAsync(target.Page, target.Namespace) is WikiPage page ? page : null);
		return JsonSerializer.Serialize(new { Pages = pages, Ownership = await Registry.GetPackageWikiPagesAsync(id) });
	}

	private async Task CleanupAsync(string id, params PackageWikiPageSpec[] targets)
	{
		if (await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord)
			(await Installer.UninstallAsync(id)).Expect<Success>();
		foreach (var target in targets.DistinctBy(page => (page.Namespace, page.Page)))
			if (await Wiki.GetBySlugAsync(target.Page, target.Namespace) is WikiPage page)
				(await Wiki.DeleteAsync(page.Id, "#1")).Expect<None>();
	}

	[Test]
	public async Task RollbackRestoresCapturedExactBindingWithoutChangingCurrentWikiContentOrOwnership()
	{
		var manifest = Manifest();
		var first = manifest.WikiPages.Single();
		var second = first with { Page = manifest.Name + "_second", Title = "Second Lifecycle Entrance" };
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(first.Page)).Expect<WikiPage>();
			var upgrade = manifest with
			{
				Version = new PackageVersion(1, 1, 0),
				Wiki = [first, second],
				WikiLandingPage = new PackageWikiLandingPageSpec(second.Namespace, second.Page)
			};
			(await ApplyAsync(upgrade)).Expect<PackageApplyResult>();
			(await Wiki.UpdateAsync(original.Id, "Current staff content must survive rollback.\n", "#1")).Expect<WikiPage>();
			(await Wiki.SetTitleAsync(original.Id, "Current staff title")).Expect<WikiPage>();
			var before = await WikiStateAsync(manifest.Name, first, second);
			var rollback = (await Installer.RollbackAsync(manifest.Name, 1)).Expect<PackageRollbackResult>();
			await Assert.That(rollback.RestoredFromRevision).IsEqualTo(1);
			await Assert.That(rollback.Revision).IsEqualTo(3);
			await Assert.That((await Registry.GetInstalledPackageAsync(manifest.Name)).Expect<InstalledPackageRecord>().Version).IsEqualTo("1.0.0");
			await Assert.That(await WikiStateAsync(manifest.Name, first, second)).IsEqualTo(before);
			await AssertPublishedBindingAsync(manifest.Name, new(first.NamespaceName, first.Page, original.Id));
			await Assert.That((await Registry.GetPackageRevisionAsync(manifest.Name, rollback.Revision)).Expect<PackageRevisionRecord>().Kind)
				.IsEqualTo(PackageRevisionKind.Rollback);
		}
		finally { await CleanupAsync(manifest.Name, first, second); }
	}

	[Test]
	[Arguments("deleted")]
	[Arguments("recreated")]
	[Arguments("ownership-lost")]
	public async Task RollbackWithholdsUnavailableCapturedBindingWithoutRecreatingOrReadoptingPages(string unavailable)
	{
		var manifest = Manifest();
		var first = manifest.WikiPages.Single();
		var second = first with { Page = manifest.Name + "_second", Title = "Second Lifecycle Entrance" };
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(first.Page)).Expect<WikiPage>();
			(await ApplyAsync(manifest with
			{
				Version = new PackageVersion(1, 1, 0),
				Wiki = [first, second],
				WikiLandingPage = new PackageWikiLandingPageSpec(second.Namespace, second.Page)
			})).Expect<PackageApplyResult>();
			if (unavailable == "ownership-lost")
				await Registry.RemovePackageWikiPageAsync(manifest.Name, first.NamespaceName, first.Page);
			else
			{
				(await Wiki.DeleteAsync(original.Id, "#1")).Expect<None>();
				if (unavailable == "recreated")
				{
					var replacement = (await Wiki.CreateAsync("Independent replacement", "Never re-adopt this replacement.\n", "#1", slug: first.Page)).Expect<WikiPage>();
					await Assert.That(replacement.Id == original.Id).IsFalse();
				}
			}
			var before = await WikiStateAsync(manifest.Name, first, second);
			var rollback = (await Installer.RollbackAsync(manifest.Name, 1)).Expect<PackageRollbackResult>();
			await Assert.That(await WikiStateAsync(manifest.Name, first, second)).IsEqualTo(before);
			await Assert.That(rollback.Notes.Any(note => note.Contains("landing", StringComparison.OrdinalIgnoreCase))).IsTrue();
			await AssertPublishedBindingAsync(manifest.Name, null);
			if (unavailable != "ownership-lost") await Assert.That(await Wiki.GetByIdAsync(original.Id) is NotFound).IsTrue();
		}
		finally { await CleanupAsync(manifest.Name, first, second); }
	}

	[Test]
	public async Task RollbackOfGenuinelyMissingLegacySnapshotFieldClearsCurrentLanding()
	{
		var manifest = Manifest();
		var first = manifest.WikiPages.Single();
		var second = first with { Page = manifest.Name + "_second", Title = "Second Lifecycle Entrance" };
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			(await ApplyAsync(manifest with
			{
				Version = new PackageVersion(1, 1, 0),
				Wiki = [first, second],
				WikiLandingPage = new PackageWikiLandingPageSpec(second.Namespace, second.Page)
			})).Expect<PackageApplyResult>();
			var old = (await Registry.GetPackageRevisionAsync(manifest.Name, 1)).Expect<PackageRevisionRecord>();
			var captured = Binding(JsonSerializer.Deserialize<PackageRevisionSnapshot>(old.ManifestSnapshotJson, SnapshotJson)
				?? throw new InvalidOperationException("Missing original native revision snapshot."));
			var original = (await Wiki.GetBySlugAsync(first.Page)).Expect<WikiPage>();
			var current = (await Wiki.GetBySlugAsync(second.Page)).Expect<WikiPage>();
			var priorInstalledBinding = Binding((await Registry.GetInstalledPackageAsync(manifest.Name)).Expect<InstalledPackageRecord>());
			var json = JsonNode.Parse(old.ManifestSnapshotJson)!.AsObject();
			foreach (var property in json.Select(pair => pair.Key).Where(key => key.Equals("WikiLandingPage", StringComparison.OrdinalIgnoreCase)).ToArray())
				json.Remove(property);
			// Replace the actual existing native revision, rather than only deserializing a made-up legacy DTO.
			await Registry.AddPackageRevisionAsync(old with { ManifestSnapshotJson = json.ToJsonString() });
			var stored = (await Registry.GetPackageRevisionAsync(manifest.Name, 1)).Expect<PackageRevisionRecord>();
			await Assert.That(stored.ManifestSnapshotJson).IsEqualTo(json.ToJsonString());
			using var storedJson = JsonDocument.Parse(stored.ManifestSnapshotJson);
			await Assert.That(storedJson.RootElement.EnumerateObject()
				.Any(property => property.Name.Equals("WikiLandingPage", StringComparison.OrdinalIgnoreCase))).IsFalse();
			var before = await WikiStateAsync(manifest.Name, first, second);
			var rollback = (await Installer.RollbackAsync(manifest.Name, 1)).Expect<PackageRollbackResult>();
			await Assert.That(rollback.RestoredFromRevision).IsEqualTo(1);
			// Assert setup capability after executing rollback, so a missing future field cannot stop
			// the lifecycle operation itself from running in the behavioral RED witness.
			await Assert.That(captured).IsEqualTo(new ObservedBinding(first.NamespaceName, first.Page, original.Id));
			await Assert.That(priorInstalledBinding).IsEqualTo(new ObservedBinding(second.NamespaceName, second.Page, current.Id));
			await Assert.That(await WikiStateAsync(manifest.Name, first, second)).IsEqualTo(before);
			await AssertPublishedBindingAsync(manifest.Name, null);
		}
		finally { await CleanupAsync(manifest.Name, first, second); }
	}

	[Test]
	[Arguments("untouched")]
	[Arguments("edited")]
	[Arguments("translated")]
	public async Task UninstallRemovesInstalledLandingAndOwnershipWhilePreservingOnlyGameSurvivors(string state)
	{
		var manifest = Manifest();
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var page = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			if (state == "edited") page = (await Wiki.UpdateAsync(page.Id, "Staff-owned surviving content.\n", "#1")).Expect<WikiPage>();
			if (state == "translated")
				(await Wiki.UpsertTranslationAsync(page.Id, "fr", "Entrée conservée", "Traduction du jeu.", "#1", null, true, null)).Expect<WikiTranslation>();
			var priorBinding = Binding((await Registry.GetInstalledPackageAsync(manifest.Name)).Expect<InstalledPackageRecord>());
			await Assert.That((await Registry.GetPackageWikiPageAsync(manifest.Name, target.NamespaceName, target.Page)).Expect<PackageWikiPageRecord>().PageId)
				.IsEqualTo(page.Id);
			await Assert.That((await Wiki.GetTranslationsAsync(page.Id)).Count).IsEqualTo(state == "translated" ? 1 : 0);
			var beforePage = JsonSerializer.Serialize((await Wiki.GetByIdAsync(page.Id)).Expect<WikiPage>());
			var beforeTranslations = JsonSerializer.Serialize(await Wiki.GetTranslationsAsync(page.Id));
			(await Installer.UninstallAsync(manifest.Name)).Expect<Success>();
			await Assert.That(priorBinding).IsEqualTo(new ObservedBinding(target.NamespaceName, target.Page, page.Id));
			await Assert.That(await Registry.GetInstalledPackageAsync(manifest.Name) is NotFound).IsTrue();
			await Assert.That((await Registry.GetInstalledPackagesAsync()).Any(package => package.Id == manifest.Name)).IsFalse();
			await Assert.That(await Registry.GetPackageWikiPagesAsync(manifest.Name)).IsEmpty();
			await Assert.That(await Registry.GetPackageWikiPageAsync(manifest.Name, target.NamespaceName, target.Page) is NotFound).IsTrue();
			if (state == "untouched")
			{
				await Assert.That(await Wiki.GetByIdAsync(page.Id) is NotFound).IsTrue();
				await Assert.That(await Wiki.GetBySlugAsync(target.Page) is NotFound).IsTrue();
			}
			else
			{
				await Assert.That(JsonSerializer.Serialize((await Wiki.GetByIdAsync(page.Id)).Expect<WikiPage>())).IsEqualTo(beforePage);
				await Assert.That(JsonSerializer.Serialize(await Wiki.GetTranslationsAsync(page.Id))).IsEqualTo(beforeTranslations);
			}
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	public async Task ReinstallBindsNewImmutablePageWithoutRevivingTheOldIncarnation()
	{
		var manifest = Manifest();
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			(await Installer.UninstallAsync(manifest.Name)).Expect<Success>();
			await Assert.That(await Wiki.GetByIdAsync(original.Id) is NotFound).IsTrue();
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var replacement = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			await Assert.That(replacement.Id == original.Id).IsFalse();
			await Assert.That(replacement.MarkdownSource).IsEqualTo(original.MarkdownSource);
			await Assert.That(await Wiki.GetByIdAsync(original.Id) is NotFound).IsTrue();
			await AssertPublishedBindingAsync(manifest.Name, new(target.NamespaceName, target.Page, replacement.Id));
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SuccessfulClearThenExplicitReaddMayBindAReplacementThroughActualAdoptionOrTakeTheirs(bool conflict)
	{
		var manifest = Manifest();
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			(await ApplyAsync(manifest with { Version = new PackageVersion(1, 1, 0), Wiki = [], WikiLandingPage = null })).Expect<PackageApplyResult>();
			await AssertPublishedBindingAsync(manifest.Name, null);
			await Assert.That(await Registry.GetPackageWikiPagesAsync(manifest.Name)).IsEmpty();
			await Assert.That(await Wiki.GetByIdAsync(original.Id) is NotFound).IsTrue();
			var replacement = (await Wiki.CreateAsync(conflict ? "Game replacement" : target.Title,
				conflict ? "Independent game replacement.\n" : target.Markdown!, "#1", slug: target.Page)).Expect<WikiPage>();
			await Assert.That(replacement.Id == original.Id).IsFalse();
			var readd = manifest with { Version = new PackageVersion(1, 2, 0) };
			var change = (await Installer.PlanAsync(readd)).Wiki!.Single();
			if (conflict)
			{
				await Assert.That(change.Conflict).IsEqualTo(PackageConflictKind.AddAdd);
				(await ApplyAsync(readd, new PackageConflictDecision(change.TargetRef, change.Page, PackageConflictResolution.TakeTheirs))).Expect<PackageApplyResult>();
			}
			else
			{
				await Assert.That(change.Action).IsEqualTo(PackageWikiAction.Adopt);
				(await ApplyAsync(readd)).Expect<PackageApplyResult>();
			}
			var current = (await Wiki.GetByIdAsync(replacement.Id)).Expect<WikiPage>();
			await Assert.That(current.MarkdownSource).IsEqualTo(target.Markdown);
			await Assert.That(await Wiki.GetByIdAsync(original.Id) is NotFound).IsTrue();
			await AssertPublishedBindingAsync(manifest.Name, new(target.NamespaceName, target.Page, replacement.Id));
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}
}
