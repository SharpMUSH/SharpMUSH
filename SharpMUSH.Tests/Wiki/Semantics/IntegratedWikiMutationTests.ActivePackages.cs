using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Package operations on an active projection: only a coordinated operation is admitted, a package's pages
/// project as its portable source while it owns them, and nothing that would change a visibility class is.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private async Task<SemanticWikiPrototypeHost> ActiveHostAsync()
	{
		var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		if (!Settled(host)) throw new InvalidOperationException("Activation did not certify the empty package history.");
		return host;
	}

	private static PackageManifest WikiPackage(string id, string pages) => new PackageManifestService().ParseManifest($$"""
		format: 1.4
		package: {{id}}
		version: "1.0.0"
		wiki:
		{{pages}}
		""") switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	private static string RoadPage(string id, string requires = "      edit: [wiki.admin]")
		=> $"""
			  - page: main:{id}_road
			    title: Coast Road
			    requires:
			{requires}
			    markdown: The road runs north.
			""";

	private static IPackageRegistryService Packages(SemanticWikiPrototypeHost host) => host.Services.GetRequiredService<LightningDatabase>();

	[Test]
	public async Task AnActiveCoordinatedInstallProjectsItsPageAsThePackagesAndUninstallReleasesIt()
	{
		await using var host = await ActiveHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var outcome = await Coordinated(host, _ => installer.ApplyAsync(WikiPackage("active-road", RoadPage("active")), MatrixRequest()));
		await Assert.That(outcome.Value is PackageOperationRan<Result<PackageApplyResult>> { Result.Value: PackageApplyResult }).IsTrue();
		await Assert.That(Settled(host)).IsTrue();

		var row = (await Packages(host).GetPackageWikiPagesAsync("active-road")).Single();
		var page = (await wiki.GetBySlugAsync("active_road", WikiNamespace.Main)).Expect<WikiPage>();
		await Assert.That(row.PageId).IsEqualTo(page.Id);
		var owned = ObserveProjection(provider, page.Id);
		await Assert.That(owned.State.Source is PortableWikiSource { Key: var key } && key == new PortableKey("active-road", row.Namespace, row.Slug)).IsTrue();
		// Edit and delete requirements are plain rows: the package's page has no read rule.
		await Assert.That(owned.State.State.Summary.Visibility.RuleIds).IsEmpty();

		// The package's page is the package's: neither a delete nor a reimport takes it from the package.
		await Assert.That(async () => await wiki.DeleteAsync(page.Id, "#1")).Throws<WikiFixtureMutationException>();
		await Assert.That(await provider.ImportSemanticFixtureFactsAsync([new(page.Id, new(AtomicWikiLayout.Schema.Version,
			[new(new(page.Id), "pf2e:level", 2L, QueryEvidence)]))], CancellationToken.None)).IsEqualTo(1);
		await Assert.That(ObserveProjection(provider, page.Id).State.Source is PortableWikiSource).IsTrue();

		var uninstalled = await Coordinated(host, _ => installer.UninstallAsync("active-road"));
		await Assert.That(uninstalled.Value is PackageOperationRan<Result<Success>>).IsTrue();
		await Assert.That(await Packages(host).GetPackageWikiPagesAsync("active-road")).IsEmpty();
		var released = ObserveProjection(provider, page.Id);
		await Assert.That(released.State.Source is LocalWikiSource).IsTrue();
		await Assert.That(await wiki.GetByIdAsync(page.Id) is WikiPage).IsFalse();
		await Assert.That(released.State.State.Summary.Deleted).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AnUncoordinatedActiveInstallIsRefusedBeforeAnyWrite(bool read)
	{
		await using var host = await ActiveHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var before = Task3Snapshot(provider, Tables.Pkg);
		var manifest = WikiPackage("active-refused", RoadPage("refused", read ? "      view: [wiki.admin]" : "      edit: [wiki.admin]"));
		var result = await installer.ApplyAsync(manifest, MatrixRequest());
		await Assert.That(result.Value is Error<string> { Value: var reason } && reason.Contains("unavailable", StringComparison.Ordinal)).IsTrue();
		// The installer wrote nothing, and neither did any coordinator.
		static bool Compared(string row) => !IsFixtureVersionRow(row)
			&& !row.StartsWith(Tables.Meta.Name + ":" + Convert.ToHexString(LightningStore.FixturePackageStateKeyBytes) + ":", StringComparison.Ordinal);
		await Assert.That(Task3Snapshot(provider, Tables.Pkg).Where(Compared)).IsEquivalentTo(before.Where(Compared));
		await Assert.That(PackageState(provider)[5]).IsEqualTo((byte)1);
		await host.CloseAsync();
	}

	[Test]
	public async Task EditAndDeleteRequirementsAreRowWritesThatChangeNoDescriptor()
	{
		await using var host = await ActiveHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Requirement target", "# Target", "#1")).Expect<WikiPage>();
		var descriptor = ObserveProjection(provider, page.Id).SummaryBytes;
		var guarded = new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Edit] = ["wiki.admin"], [WikiAction.Delete] = ["wiki.admin"] };
		foreach (var target in new[] { WikiRuleTarget.ForPage(page.Id), WikiRuleTarget.ForNamespace("main") })
			await Assert.That(await wiki.SetRequirementsAsync(target, guarded, "#1") is None).IsTrue();
		await Assert.That((await wiki.GetRequirementsAsync()).HasPageRules(page.Id)).IsTrue();
		await Assert.That(ObserveProjection(provider, page.Id).SummaryBytes).IsEquivalentTo(descriptor);
		await host.CloseAsync();
	}

	[Test]
	public async Task AFailedActiveOperationLeavesTheWorldUnknown()
	{
		await using var host = await ActiveHostAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		await Assert.That(async () => await Coordinated<int>(host, async _ =>
		{
			await installer.ApplyAsync(WikiPackage("active-failed", RoadPage("failed")), MatrixRequest());
			throw new ArithmeticException("after commit");
		})).Throws<ArithmeticException>();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())[5]).IsEqualTo((byte)0);
		await Assert.That(await ProbeReadAsync(host) is SemanticReadResult<string>.Incomplete).IsTrue();
		// No verify operation: only a later coordinated operation that itself settles certifies the world again.
		await Coordinated(host, _ => installer.UninstallAsync("active-failed"));
		await Assert.That(Settled(host)).IsTrue();
		await Assert.That(await ProbeReadAsync(host) is SemanticReadResult<string>.Certified).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	public async Task AFactImportIsRefusedWhileAPackageOperationIsRetained()
	{
		await using var host = await ActiveHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Import target", "# Target", "#1")).Expect<WikiPage>();
		SemanticFixtureFactImport[] import = [new(page.Id, new(AtomicWikiLayout.Schema.Version, [new(new(page.Id), "pf2e:level", 1L, QueryEvidence)]))];
		var owner = ((SemanticWikiPackageOperationRunner)host.Services.GetRequiredService<IPackageOperationRunner>()).Owner;
		var held = await provider.BeginRetainedSemanticFixturePackageOperationAsync(owner, CancellationToken.None, coordinated: true);
		try
		{
			await Assert.That(async () => await provider.ImportSemanticFixtureFactsAsync(import, CancellationToken.None))
				.Throws<SemanticFixtureOperationInProgressException>();
		}
		finally { await held.DisposeAsync(); }
		// Its BEGIN left the world UNKNOWN; once a coordinated operation settles it again, the import is admitted.
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		await Coordinated(host, _ => installer.ApplyAsync(MatrixManifest(), MatrixRequest()));
		await Assert.That(Settled(host)).IsTrue();
		await Assert.That(await provider.ImportSemanticFixtureFactsAsync(import, CancellationToken.None)).IsEqualTo(1);
		await host.CloseAsync();
	}
}
