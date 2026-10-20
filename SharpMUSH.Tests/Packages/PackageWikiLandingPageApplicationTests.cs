using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>Ordinary body upgrades retain the exact native wiki entrance for both supported package kinds.</summary>
public class PackageWikiLandingPageApplicationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IApplicationRegistryService Applications => (IApplicationRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private const string DesiredBody = "# Entrance\nThe northern road.\nTwo inns halfway.\n";
	private const string LocalTitle = "The Game's Orientation";

	private static PackageManifest Manifest(string id, PackageKind kind)
	{
		var application = kind == PackageKind.Application ? $$"""
			kind: application
			application:
			  slug: {{id}}-widget
			  display_name: Landing application fixture
			  type: widget
			  schema_url: http/{{id}}/schema
			  minimum_role: player
			  zones: [MainContent]
			""" : string.Empty;
		return new PackageManifestService().ParseManifest($$"""
			format: 1.14
			package: {{id}}
			version: 1.0.0
			wiki_landing_page: main:{{id}}_entrance
			wiki:
			  - page: main:{{id}}_entrance
			    title: Package Entrance
			    markdown: |
			      # Entrance
			      The northern road.
			      One inn halfway.
			{{application}}
			""").Expect<ParsedPackageManifest>().Manifest;
	}

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", manifest.Name + "/", "commit-" + manifest.Version, "main"),
			new Dictionary<string, string>(), []));

	// Semantic JSON observation keeps the failure about the missing installed identity behavior,
	// without requiring future binding symbols or checking mere property presence.
	private sealed record ObservedBinding(string? Namespace, string? Slug, string? PageId);

	private static ObservedBinding? Binding<T>(T value)
	{
		var json = JsonSerializer.SerializeToElement(value);
		if (!json.TryGetProperty("WikiLandingPage", out var binding) || binding.ValueKind != JsonValueKind.Object) return null;
		static string? Text(JsonElement element, string key)
			=> element.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
		return new ObservedBinding(Text(binding, "Namespace"), Text(binding, "Slug"), Text(binding, "PageId"));
	}

	private async Task AssertApplicationAsync(string id)
	{
		var application = (await Applications.GetApplicationAsync(id + "-widget")).Expect<RegisteredApplication>();
		await Assert.That(application.Slug).IsEqualTo(id + "-widget");
		await Assert.That(application.OwningPackage).IsEqualTo(id);
		await Assert.That(application.Kind).IsEqualTo(ApplicationKind.Widget);
		await Assert.That(application.SchemaUrl).IsEqualTo("http/" + id + "/schema");
		await Assert.That(application.MinimumRole).IsEqualTo(PortalRole.Player);
		await Assert.That((application.Zones ?? []).SequenceEqual([WidgetZone.MainContent])).IsTrue();
	}

	private async Task AssertOrdinaryBodyUpgradeAsync(PackageKind kind)
	{
		var id = "landing-body-" + Guid.NewGuid().ToString("N")[..12];
		var manifest = Manifest(id, kind);
		var target = manifest.WikiPages.Single();
		try
		{
			// Seed successfully through the actual installer. Do not assert future binding metadata
			// before the ordinary-upgrade witness has been reached.
			(await ApplyAsync(manifest)).Expect<PackageApplyResult>();
			var initialOwner = (await Registry.GetPackageWikiPageAsync(id, target.NamespaceName, target.Page)).Expect<PackageWikiPageRecord>();
			var original = (await Wiki.GetByIdAsync(initialOwner.PageId)).Expect<WikiPage>();
			await Assert.That(original.MarkdownSource).IsEqualTo(target.Markdown);
			if (kind == PackageKind.Application) await AssertApplicationAsync(id);

			(await Wiki.SetTitleAsync(original.Id, LocalTitle)).Expect<WikiPage>();
			var upgrade = manifest with
			{
				Version = new PackageVersion(1, 1, 0),
				Wiki = [target with { Markdown = DesiredBody }]
			};
			var plan = await Installer.PlanAsync(upgrade);
			await Assert.That(plan.IsBlocked).IsFalse();
			await Assert.That(plan.HasConflicts).IsFalse();
			await Assert.That(plan.Wiki!.Single().Action).IsEqualTo(PackageWikiAction.Update);
			var applied = (await ApplyAsync(upgrade)).Expect<PackageApplyResult>();
			await Assert.That(applied.Revision).IsEqualTo(2);

			var owner = (await Registry.GetPackageWikiPageAsync(id, target.NamespaceName, target.Page)).Expect<PackageWikiPageRecord>();
			var current = (await Wiki.GetByIdAsync(owner.PageId)).Expect<WikiPage>();
			var routed = (await Wiki.GetBySlugAsync(target.Page, WikiNamespace.Main)).Expect<WikiPage>();
			await Assert.That(owner.PackageId).IsEqualTo(id);
			await Assert.That(owner.Namespace).IsEqualTo(target.NamespaceName);
			await Assert.That(owner.Slug).IsEqualTo(target.Page);
			await Assert.That(owner.PageId).IsEqualTo(original.Id);
			await Assert.That(owner.Markdown).IsEqualTo(DesiredBody);
			await Assert.That(owner.Title).IsEqualTo(target.Title);
			await Assert.That(owner.BaselineVersion).IsEqualTo("1.1.0");
			await Assert.That(current.Id).IsEqualTo(original.Id);
			await Assert.That(current.Namespace).IsEqualTo(target.NamespaceName);
			await Assert.That(current.Slug).IsEqualTo(target.Page);
			await Assert.That(current.Title).IsEqualTo(LocalTitle);
			await Assert.That(current.MarkdownSource).IsEqualTo(DesiredBody);
			await Assert.That(routed.Id).IsEqualTo(original.Id);

			var expected = new ObservedBinding(target.NamespaceName, target.Page, original.Id);
			var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
			await Assert.That(installed.Kind).IsEqualTo(kind);
			await Assert.That(installed.Version).IsEqualTo("1.1.0");
			await Assert.That(installed.CurrentRevision).IsEqualTo(2);
			await Assert.That(Binding(installed)).IsEqualTo(expected);
			await Assert.That(Binding((await Registry.GetInstalledPackagesAsync()).Single(package => package.Id == id))).IsEqualTo(expected);
			var revision = (await Registry.GetPackageRevisionAsync(id, installed.CurrentRevision)).Expect<PackageRevisionRecord>();
			await Assert.That(revision.Kind).IsEqualTo(PackageRevisionKind.Upgrade);
			var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(revision.ManifestSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
				?? throw new InvalidOperationException("The upgraded package has no revision snapshot.");
			await Assert.That(snapshot.Version).IsEqualTo("1.1.0");
			await Assert.That(Binding(snapshot)).IsEqualTo(expected);
			if (kind == PackageKind.Application) await AssertApplicationAsync(id);

			(await Installer.UninstallAsync(id)).Expect<Success>();
			await Assert.That(await Registry.GetInstalledPackageAsync(id) is NotFound).IsTrue();
			await Assert.That(await Registry.GetPackageWikiPagesAsync(id)).IsEmpty();
			if (kind == PackageKind.Application)
			{
				await Assert.That(await Applications.GetApplicationAsync(id + "-widget") is NotFound).IsTrue();
			}
			// The local title makes the page a retained game page; removing registration/ownership
			// must not destroy it or change the successful body update.
			var retained = (await Wiki.GetByIdAsync(original.Id)).Expect<WikiPage>();
			await Assert.That(retained.Title).IsEqualTo(LocalTitle);
			await Assert.That(retained.MarkdownSource).IsEqualTo(DesiredBody);
		}
		finally
		{
			if (await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord)
			{
				(await Installer.UninstallAsync(id)).Expect<Success>();
			}
			if (await Wiki.GetBySlugAsync(target.Page, WikiNamespace.Main) is WikiPage remaining)
			{
				(await Wiki.DeleteAsync(remaining.Id, "#1")).Expect<None>();
			}
		}
	}

	[Test]
	public Task SoftcodeNoConflictBodyUpdatePreservesBoundIdentityAndLocalTitle()
		=> AssertOrdinaryBodyUpgradeAsync(PackageKind.Softcode);

	[Test]
	public Task ApplicationNoConflictBodyUpdatePreservesBoundIdentityAndRegistrationLifecycle()
		=> AssertOrdinaryBodyUpgradeAsync(PackageKind.Application);
}
