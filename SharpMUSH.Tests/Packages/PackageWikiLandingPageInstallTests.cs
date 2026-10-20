using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>Collection entrances bind the actual page incarnation produced by the native installer.</summary>
public class PackageWikiLandingPageInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private const string OriginalBody = "# Entrance\nThe northern road.\nOne inn halfway.\nThe lighthouse at the end.\n";
	private static string FreshId() => "landing-" + Guid.NewGuid().ToString("N")[..12];

	private static PackageManifest Manifest(string id, string version = "1.0.0", WikiNamespace ns = WikiNamespace.Main)
		=> new PackageManifestService().ParseManifest($$"""
			format: 1.14
			package: {{id}}
			version: {{version}}
			wiki_landing_page: {{WikiHelpers.NamespaceName(ns)}}:{{id}}_entrance
			wiki:
			  - page: {{WikiHelpers.NamespaceName(ns)}}:{{id}}_entrance
			    title: Package Entrance
			    markdown: |
			      # Entrance
			      The northern road.
			      One inn halfway.
			      The lighthouse at the end.
			""").Expect<ParsedPackageManifest>().Manifest;

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, params PackageConflictDecision[] decisions)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", manifest.Name + "/", "commit-" + manifest.Version, "main"),
			new Dictionary<string, string>(), decisions));

	private async Task<Result<PackageApplyResult>> ApplyDecidingAsync(PackageManifest manifest, PackageConflictResolution resolution)
	{
		var plan = await Installer.PlanAsync(manifest);
		var decisions = (plan.Wiki ?? []).Where(change => change.Action == PackageWikiAction.Conflict)
			.Select(change => new PackageConflictDecision(change.TargetRef, change.Page, resolution)).ToArray();
		return await ApplyAsync(manifest, decisions);
	}

	// Observe the additive metadata by semantic JSON values until its production type exists. All expected IDs
	// below come from real ownership/live-page reads, rather than a fabricated binding or a property-presence check.
	private sealed record ObservedBinding(string? Namespace, string? Slug, string? PageId);

	private static ObservedBinding? Binding<T>(T value)
	{
		var json = JsonSerializer.SerializeToElement(value);
		if (!json.TryGetProperty("WikiLandingPage", out var binding) || binding.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		static string? Text(JsonElement element, string name)
			=> element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
		return new ObservedBinding(Text(binding, "Namespace"), Text(binding, "Slug"), Text(binding, "PageId"));
	}

	private async Task AssertBindingAsync(string id, PackageWikiPageSpec target, string expectedPageId, string version)
	{
		var owner = (await Registry.GetPackageWikiPageAsync(id, target.NamespaceName, target.Page)).Expect<PackageWikiPageRecord>();
		var live = (await Wiki.GetByIdAsync(owner.PageId)).Expect<WikiPage>();
		await Assert.That(owner.PageId).IsEqualTo(expectedPageId);
		await Assert.That(live.Id).IsEqualTo(expectedPageId);
		await Assert.That(live.Namespace).IsEqualTo(target.NamespaceName);
		await Assert.That(live.Slug).IsEqualTo(target.Page);
		var expected = new ObservedBinding(target.NamespaceName, target.Page, expectedPageId);
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Version).IsEqualTo(version);
		await Assert.That(Binding(installed)).IsEqualTo(expected);
		await Assert.That(Binding((await Registry.GetInstalledPackagesAsync()).Single(package => package.Id == id))).IsEqualTo(expected);
		var revision = (await Registry.GetPackageRevisionAsync(id, installed.CurrentRevision)).Expect<PackageRevisionRecord>();
		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
			?? throw new InvalidOperationException("The installed revision has no snapshot.");
		await Assert.That(Binding(snapshot)).IsEqualTo(expected);
	}

	private async Task AssertNoBindingAsync(string id)
	{
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(Binding(installed)).IsNull();
		var revision = (await Registry.GetPackageRevisionAsync(id, installed.CurrentRevision)).Expect<PackageRevisionRecord>();
		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
			?? throw new InvalidOperationException("The installed revision has no snapshot.");
		await Assert.That(Binding(snapshot)).IsNull();
	}

	private async Task<string> StateAsync(string id)
		=> JsonSerializer.Serialize(new
		{
			Installed = await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord installed ? installed : null,
			Revisions = await Registry.GetPackageRevisionsAsync(id),
			Owned = await Registry.GetPackageWikiPagesAsync(id)
		});

	private static string PageContent(WikiPage page) => JsonSerializer.Serialize(new
	{
		page.Id,
		page.Namespace,
		page.Slug,
		page.Title,
		page.MarkdownSource,
		page.RenderedHtml,
		page.PlainText,
		page.Published,
		page.Categories,
		page.SourceLocale
	});

	private async Task CleanupAsync(string id, params PackageWikiPageSpec[] pages)
	{
		if (await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord)
		{
			(await Installer.UninstallAsync(id)).Expect<Success>();
		}

		foreach (var target in pages.DistinctBy(page => (page.Namespace, page.Page)))
		{
			if (await Wiki.GetBySlugAsync(target.Page, target.Namespace) is WikiPage live)
			{
				(await Wiki.DeleteAsync(live.Id, "#1")).Expect<None>();
			}
		}
	}

	[Test]
	[Arguments(WikiNamespace.Main)]
	[Arguments(WikiNamespace.Help)]
	[Arguments(WikiNamespace.Category)]
	public async Task InstallBindsActualOwnedPageIdAndSnapshot(WikiNamespace ns)
	{
		var manifest = Manifest(FreshId(), ns: ns);
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var live = (await Wiki.GetBySlugAsync(target.Page, ns)).Expect<WikiPage>();
			await AssertBindingAsync(manifest.Name, target, live.Id, "1.0.0");
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	public async Task ExactExistingPageAdoptionBindsItsActualId()
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		var existing = (await Wiki.CreateAsync(target.Title, target.Markdown!, "#1", slug: target.Page)).Expect<WikiPage>();
		try
		{
			await Assert.That((await Installer.PlanAsync(manifest)).Wiki!.Single().Action).IsEqualTo(PackageWikiAction.Adopt);
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			await AssertBindingAsync(manifest.Name, target, existing.Id, "1.0.0");
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	public async Task KeepMineCannotBindAnUnownedGamePage()
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		var existing = (await Wiki.CreateAsync("The Game's Page", "Independent game content.\n", "#1", slug: target.Page)).Expect<WikiPage>();
		try
		{
			var change = (await Installer.PlanAsync(manifest)).Wiki!.Single();
			await Assert.That(change.Conflict).IsEqualTo(PackageConflictKind.AddAdd);
			var result = await ApplyAsync(manifest, new PackageConflictDecision(change.TargetRef, change.Page, PackageConflictResolution.KeepMine));
			var error = result.Expect<Error<string>>();
			await Assert.That(error.Value.ToLowerInvariant()).Contains("landing");
			await Assert.That(await Registry.GetInstalledPackageAsync(manifest.Name) is NotFound).IsTrue();
			await Assert.That(await Registry.GetPackageWikiPagesAsync(manifest.Name)).IsEmpty();
			await Assert.That(await Registry.GetPackageRevisionsAsync(manifest.Name)).IsEmpty();
			await Assert.That(JsonSerializer.Serialize((await Wiki.GetByIdAsync(existing.Id)).Expect<WikiPage>()))
				.IsEqualTo(JsonSerializer.Serialize(existing));
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	[Arguments(PackageConflictResolution.KeepMine)]
	[Arguments(PackageConflictResolution.TakeTheirs)]
	public async Task ContentConflictAndLocalTitleEditRetainIncarnation(PackageConflictResolution resolution)
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		try
		{
			// Seed through the actual install without a future-metadata assertion masking the upgrade witness.
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			var localBody = OriginalBody.Replace("One inn halfway.", "Two inns, by staff.");
			(await Wiki.UpdateAsync(original.Id, localBody, "#1")).Expect<WikiPage>();
			(await Wiki.SetTitleAsync(original.Id, "The Game's Entrance")).Expect<WikiPage>();
			var desiredBody = OriginalBody.Replace("One inn halfway.", "One inn, by the package.");
			var upgrade = manifest with
			{
				Version = new PackageVersion(1, 1, 0),
				Wiki = [target with { Markdown = desiredBody }]
			};
			var change = (await Installer.PlanAsync(upgrade)).Wiki!.Single();
			await Assert.That(change.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
			(await ApplyAsync(upgrade, new PackageConflictDecision(change.TargetRef, change.Page, resolution))).Expect<PackageApplyResult>();
			var current = (await Wiki.GetByIdAsync(original.Id)).Expect<WikiPage>();
			await Assert.That(current.Title).IsEqualTo("The Game's Entrance");
			await Assert.That(current.MarkdownSource).IsEqualTo(resolution == PackageConflictResolution.KeepMine ? localBody : desiredBody);
			await Assert.That((await Registry.GetPackageWikiPageAsync(manifest.Name, "main", target.Page)).Expect<PackageWikiPageRecord>().Markdown)
				.IsEqualTo(desiredBody);
			await AssertBindingAsync(manifest.Name, target, original.Id, "1.1.0");
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	public async Task ExplicitTargetChangeBindsTheNewOwnedPageWithoutDeletingTheFormer()
	{
		var manifest = Manifest(FreshId());
		var first = manifest.WikiPages.Single();
		var second = first with { Page = manifest.Name + "_second", Title = "Second Entrance" };
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
			var current = (await Wiki.GetBySlugAsync(second.Page)).Expect<WikiPage>();
			await Assert.That(current.Id == original.Id).IsFalse();
			await Assert.That((await Wiki.GetByIdAsync(original.Id)).Expect<WikiPage>().MarkdownSource).IsEqualTo(original.MarkdownSource);
			await Assert.That((await Registry.GetPackageWikiPagesAsync(manifest.Name)).Count).IsEqualTo(2);
			await AssertBindingAsync(manifest.Name, second, current.Id, "1.1.0");
		}
		finally { await CleanupAsync(manifest.Name, first, second); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OmissionClearsLandingWhileTheOwnedPageSurvives(bool legacyFormat)
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			var upgrade = manifest with
			{
				Version = new PackageVersion(1, 1, 0),
				Format = legacyFormat ? new PackageFormatVersion(1, 4) : manifest.Format,
				WikiLandingPage = null
			};
			(await ApplyAsync(upgrade)).Expect<PackageApplyResult>();
			await Assert.That((await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>().Id).IsEqualTo(original.Id);
			await Assert.That((await Registry.GetPackageWikiPageAsync(manifest.Name, "main", target.Page)).Expect<PackageWikiPageRecord>().PageId)
				.IsEqualTo(original.Id);
			await AssertNoBindingAsync(manifest.Name);
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SameAuthoredTargetCannotSilentlyRebindReplacement(bool recreateBeforeApply)
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			(await Wiki.DeleteAsync(original.Id, "#1")).Expect<None>();
			WikiPage? replacement = null;
			if (recreateBeforeApply)
			{
				replacement = (await Wiki.CreateAsync("Game Replacement", "Unrelated game content.\n", "#1", slug: target.Page)).Expect<WikiPage>();
				await Assert.That(replacement.Id == original.Id).IsFalse();
			}

			var upgrade = manifest with
			{
				Version = new PackageVersion(1, 1, 0),
				Wiki = [target with { Markdown = OriginalBody + "The package changed.\n" }]
			};
			var before = await StateAsync(manifest.Name);
			var beforePage = replacement is null ? null : PageContent(replacement);
			var beforeRequirements = replacement is null ? null
				: JsonSerializer.Serialize((await Wiki.GetRequirementsAsync()).For(WikiRuleTarget.ForPage(replacement.Id)));
			// Both AddAdd and DeleteModify require TakeTheirs. Supplying it ensures an unrelated
			// unresolved-conflict refusal cannot masquerade as the missing incarnation guard.
			var result = await ApplyDecidingAsync(upgrade, PackageConflictResolution.TakeTheirs);
			var error = result.Expect<Error<string>>();
			await Assert.That(error.Value.ToLowerInvariant()).Contains("landing");
			await Assert.That(await StateAsync(manifest.Name)).IsEqualTo(before);
			await Assert.That(await Wiki.GetByIdAsync(original.Id) is NotFound).IsTrue();
			var afterPage = await Wiki.GetBySlugAsync(target.Page) is WikiPage live ? live : null;
			await Assert.That(afterPage is null ? null : PageContent(afterPage)).IsEqualTo(beforePage);
			var afterRequirements = afterPage is null ? null
				: JsonSerializer.Serialize((await Wiki.GetRequirementsAsync()).For(WikiRuleTarget.ForPage(afterPage.Id)));
			await Assert.That(afterRequirements).IsEqualTo(beforeRequirements);
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	public async Task RemovedDesiredTargetRefusesBeforeWrites()
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			var before = await StateAsync(manifest.Name);
			var invalid = manifest with { Version = new PackageVersion(1, 1, 0), Wiki = [] };
			var result = await ApplyAsync(invalid);
			var error = result.Expect<Error<string>>();
			await Assert.That(error.Value).Contains("wiki_landing_page");
			await Assert.That(await StateAsync(manifest.Name)).IsEqualTo(before);
			await Assert.That(JsonSerializer.Serialize((await Wiki.GetByIdAsync(original.Id)).Expect<WikiPage>()))
				.IsEqualTo(JsonSerializer.Serialize(original));
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	[Arguments("undeclared")]
	[Arguments("old-format")]
	[Arguments("wrong-namespace")]
	[Arguments("noncanonical")]
	[Arguments("duplicate")]
	public async Task ProgrammaticInvalidMetadataRefusesBeforePackageInstallWrites(string invalidCase)
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		var invalid = invalidCase switch
		{
			"undeclared" => manifest with { WikiLandingPage = new PackageWikiLandingPageSpec(WikiNamespace.Main, manifest.Name + "_missing") },
			"old-format" => manifest with { Format = new PackageFormatVersion(1, 13) },
			"wrong-namespace" => manifest with { WikiLandingPage = new PackageWikiLandingPageSpec(WikiNamespace.Help, target.Page) },
			"noncanonical" => manifest with { WikiLandingPage = new PackageWikiLandingPageSpec(WikiNamespace.Main, target.Page.ToUpperInvariant()) },
			"duplicate" => manifest with { Wiki = [target, target] },
			_ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
		};
		try
		{
			var result = await ApplyAsync(invalid);
			var error = result.Expect<Error<string>>();
			await Assert.That(error.Value).Contains("wiki_landing_page");
			await Assert.That(await Registry.GetInstalledPackageAsync(manifest.Name) is NotFound).IsTrue();
			await Assert.That(await Registry.GetPackageWikiPagesAsync(manifest.Name)).IsEmpty();
			await Assert.That(await Registry.GetPackageRevisionsAsync(manifest.Name)).IsEmpty();
			await Assert.That(await Wiki.GetBySlugAsync(target.Page) is NotFound).IsTrue();
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	public async Task LegacyManifestWithoutLandingStillInstallsNormally()
	{
		var manifest = Manifest(FreshId()) with { Format = new PackageFormatVersion(1, 4), WikiLandingPage = null };
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var live = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			await Assert.That((await Registry.GetPackageWikiPageAsync(manifest.Name, "main", target.Page)).Expect<PackageWikiPageRecord>().PageId)
				.IsEqualTo(live.Id);
			await AssertNoBindingAsync(manifest.Name);
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LostOwnershipMayReadoptOnlyTheOriginalIncarnation(bool replace)
	{
		var manifest = Manifest(FreshId());
		var target = manifest.WikiPages.Single();
		try
		{
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var original = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			await Registry.RemovePackageWikiPageAsync(manifest.Name, target.NamespaceName, target.Page);
			if (replace)
			{
				(await Wiki.DeleteAsync(original.Id, "#1")).Expect<None>();
				var replacement = (await Wiki.CreateAsync(target.Title, target.Markdown!, "#1", slug: target.Page)).Expect<WikiPage>();
				await Assert.That(replacement.Id == original.Id).IsFalse();
			}

			var desired = manifest with { Version = new PackageVersion(1, 1, 0) };
			var before = await StateAsync(manifest.Name);
			var live = (await Wiki.GetBySlugAsync(target.Page)).Expect<WikiPage>();
			var plan = await Installer.PlanAsync(desired);
			await Assert.That(plan.Wiki!.Single().Action).IsEqualTo(PackageWikiAction.Adopt);
			if (replace)
			{
				await Assert.That(plan.IsBlocked).IsTrue();
				await Assert.That((await ApplyAsync(desired)).Expect<Error<string>>().Value.ToLowerInvariant()).Contains("landing");
				await Assert.That(await StateAsync(manifest.Name)).IsEqualTo(before);
				await Assert.That(JsonSerializer.Serialize((await Wiki.GetByIdAsync(live.Id)).Expect<WikiPage>())).IsEqualTo(JsonSerializer.Serialize(live));
			}
			else
			{
				await Assert.That(plan.IsBlocked).IsFalse();
				(await ApplyAsync(desired)).Expect<PackageApplyResult>();
				await AssertBindingAsync(manifest.Name, target, original.Id, "1.1.0");
			}
		}
		finally { await CleanupAsync(manifest.Name, target); }
	}
}
