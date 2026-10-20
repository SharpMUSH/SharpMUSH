using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>Administrators can review entrance changes without changing installed state.</summary>
public class PackageWikiLandingPagePlanTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static PackageManifest Manifest(string id) => new PackageManifestService().ParseManifest($$"""
		format: 1.14
		package: {{id}}
		version: 1.0.0
		wiki_landing_page: main:{{id}}_start
		wiki:
		  - page: main:{{id}}_start
		    title: Begin Here
		    markdown: First instructions.
		  - page: main:{{id}}_next
		    title: Further Instructions
		    markdown: Later instructions.
		""").Expect<ParsedPackageManifest>().Manifest;

	private async Task<string> StateAsync(string id, PackageManifest manifest)
	{
		var pages = new List<WikiPage>();
		foreach (var spec in manifest.WikiPages)
		{
			if (await Wiki.GetBySlugAsync(spec.Page, spec.Namespace) is WikiPage page) pages.Add(page);
		}

		return JsonSerializer.Serialize(new
		{
			Installed = await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord installed ? installed : null,
			Revisions = await Registry.GetPackageRevisionsAsync(id),
			Ownership = await Registry.GetPackageWikiPagesAsync(id),
			Pages = pages
		});
	}

	[Test]
	[Arguments("add")]
	[Arguments("change")]
	[Arguments("remove")]
	[Arguments("invalid")]
	public async Task PlanExplainsEntranceChangesAndLeavesActualPackageAndPagesUntouched(string operation)
	{
		var id = "landing-plan-" + Guid.NewGuid().ToString("N")[..12];
		var manifest = Manifest(id);
		try
		{
			if (operation != "add")
			{
				(await Installer.ApplyAsync(manifest, new PackageApplyRequest(
					new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", id + "/", "commit-1", "main"),
					new Dictionary<string, string>(), []))).Expect<PackageApplyResult>();
			}

			var desired = operation switch
			{
				"change" => manifest with { Version = new PackageVersion(1, 1, 0), WikiLandingPage = new PackageWikiLandingPageSpec(WikiNamespace.Main, id + "_next") },
				"remove" => manifest with { Version = new PackageVersion(1, 1, 0), WikiLandingPage = null },
				"invalid" => manifest with { Version = new PackageVersion(1, 1, 0), WikiLandingPage = new PackageWikiLandingPageSpec(WikiNamespace.Main, id + "_missing") },
				_ => manifest
			};
			var before = await StateAsync(id, manifest);
			var plan = await Installer.PlanAsync(desired);
			await Assert.That(await StateAsync(id, manifest)).IsEqualTo(before);
			var notes = string.Join("\n", plan.Notes);
			await Assert.That(notes.ToLowerInvariant()).Contains("landing");
			if (operation == "invalid")
			{
				await Assert.That(plan.IsBlocked).IsTrue();
				await Assert.That(notes).Contains("wiki_landing_page");
			}
			else
			{
				await Assert.That(plan.IsBlocked).IsFalse();
				await Assert.That(notes).Contains("main:" + id + "_start");
				if (operation == "change") await Assert.That(notes).Contains("main:" + id + "_next");
			}
		}
		finally
		{
			if (await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord)
				(await Installer.UninstallAsync(id)).Expect<Success>();
		}
	}
}
