using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// The offline catalogue: the packages embedded in the server assembly, reachable through the
/// reserved <c>bundled</c> remote. These drive <see cref="PackagesController"/> directly (the
/// pattern used by the other controller tests) because what is being tested is the controller's
/// own routing between embedded resources and git, not HTTP.
///
/// The point of the feature is that a package can ship without being installed, so the first
/// assertion of the install test is that first boot did NOT install wiki-reader.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class BundledCatalogueTests(ServerWebAppFactory factory)
{
	private IPackageRegistryService Registry => factory.Services.GetRequiredService<IPackageRegistryService>();

	private PackagesController Controller() => new(
		Registry,
		factory.Services.GetRequiredService<IPackageSourceService>(),
		factory.Services.GetRequiredService<IPackageManifestService>(),
		factory.Services.GetRequiredService<IPackageInstallService>(),
		factory.Services.GetRequiredService<IPackageAuthoringService>());

	private static T Value<T>(ActionResult<T> result) where T : class =>
		result.Value ?? (T)((ObjectResult)result.Result!).Value!;

	[Test]
	public async Task Browse_ListsTheWholeCatalogue_WithoutAConfiguredRemote()
	{
		var snapshot = Value(await Controller().Browse(BundledPackages.RemoteName, CancellationToken.None));

		await Assert.That(snapshot.RemoteName).IsEqualTo(BundledPackages.RemoteName);
		await Assert.That(snapshot.Packages.Select(p => p.PackageId ?? "<unparsable>").ToList())
			.IsEquivalentTo(BundledPackages.All.Select(d => d.PackageId).ToArray());

		var wikiReader = snapshot.Packages.Single(p => p.PackageId == "wiki-reader");
		await Assert.That(wikiReader.Path).IsEqualTo("wiki-reader");
		await Assert.That(wikiReader.Version).IsNotNull();
		await Assert.That(wikiReader.Description).IsNotNull();
	}

	/// <summary>
	/// The reserved name is always present and cannot be shadowed, edited, or removed: it is not a
	/// row in sys_remotes, so allowing a real remote to take the name would make the browse target
	/// ambiguous.
	/// </summary>
	[Test]
	public async Task TheReservedRemote_IsListedAndCannotBeEditedOrRemoved()
	{
		var controller = Controller();

		var remotes = Value(await controller.GetRemotes());
		await Assert.That(remotes.Select(r => r.Name)).Contains(BundledPackages.RemoteName);
		await Assert.That(remotes.Count(r => r.Name == BundledPackages.RemoteName)).IsEqualTo(1);

		var upsert = await controller.UpsertRemote(
			new RemoteRequest(BundledPackages.RemoteName, "https://example.invalid/repo", "community", null));
		await Assert.That(upsert).IsTypeOf<ConflictObjectResult>();

		var delete = await controller.DeleteRemote(BundledPackages.RemoteName);
		await Assert.That(delete).IsTypeOf<ConflictObjectResult>();
	}

	/// <summary>
	/// Asking for a package this build does not ship is a 404 rather than a clone attempt against
	/// "bundled:sharpmush".
	/// </summary>
	[Test]
	public async Task PlanningSomethingNotShipped_IsNotFound()
	{
		var plan = await Controller().Plan(
			new PlanRequest(BundledPackages.RemoteName, "no-such-package", null, null), CancellationToken.None);

		await Assert.That(plan.Result).IsTypeOf<NotFoundObjectResult>();
	}

	/// <summary>
	/// An update check on a package installed from the image compares against what the image ships,
	/// with no git access. Before the catalogue existed this endpoint synthesized a remote whose URL
	/// was "bundled:sharpmush" and handed it to the source service, so it answered 502 for every
	/// package installed at first boot.
	/// </summary>
	[Test]
	public async Task UpdateCheck_ForAPackageInstalledAtFirstBoot_AnswersFromTheImage()
	{
		var installed = await Registry.GetInstalledPackageAsync("common-functions");
		await Assert.That(installed.IsT0).IsTrue().Because("common-functions installs at first boot");
		await Assert.That(BundledPackages.IsCatalogueSource(installed.AsT0.SourceRepo)).IsTrue();

		var info = Value(await Controller().CheckForUpdate("common-functions", CancellationToken.None));

		await Assert.That(info.InstalledVersion).IsEqualTo(installed.AsT0.Version);
		await Assert.That(info.LatestVersion).IsEqualTo(installed.AsT0.Version);
		await Assert.That(info.UpdateAvailable).IsFalse();
		await Assert.That(info.LatestCommit).IsEqualTo(BundledPackages.SourceCommit);
	}

	/// <summary>
	/// The whole feature in one narrative: wiki-reader ships in the image, first boot leaves it
	/// alone, and an admin can install it offline through the ordinary plan/apply flow — landing a
	/// registry row indistinguishable from one bootstrap would have written.
	///
	/// It uninstalls at the end: wiki-reader puts a +wiki object in the master room, and the test
	/// session is shared, so leaving it installed would change what every later test's game looks
	/// like.
	/// </summary>
	[Test]
	public async Task WikiReader_ShipsUninstalled_AndInstallsFromTheImage()
	{
		var controller = Controller();

		var before = await Registry.GetInstalledPackageAsync("wiki-reader");
		await Assert.That(before.IsT1)
			.IsTrue()
			.Because("a package that is shipped but not flagged must not be installed at first boot");

		// Read the master room's contents FIRST, so the entry the install has to invalidate is
		// actually in the cache. Without this the staleness check below can pass for the wrong
		// reason — nothing cached, nothing stale.
		await MasterRoomContentsAsync();

		try
		{
			var plan = Value(await controller.Plan(
				new PlanRequest(BundledPackages.RemoteName, "wiki-reader", null, null), CancellationToken.None));

			await Assert.That(plan.PackageId).IsEqualTo("wiki-reader");
			await Assert.That(plan.Commit).IsEqualTo(BundledPackages.SourceCommit);
			await Assert.That(plan.Changeset.Attributes).IsNotEmpty();

			var applied = Value(await controller.Apply(
				new ApplyRequest(BundledPackages.RemoteName, "wiki-reader", null, null, null),
				CancellationToken.None));

			await Assert.That(applied.Revision).IsEqualTo(1);

			var after = await Registry.GetInstalledPackageAsync("wiki-reader");
			await Assert.That(after.IsT0).IsTrue().Because("apply must install it");
			await Assert.That(after.AsT0.SourceRepo).IsEqualTo(BundledPackages.SourceRepo);
			await Assert.That(after.AsT0.SourcePath).IsEqualTo("wiki-reader");
			await Assert.That(after.AsT0.InstalledCommit).IsEqualTo(BundledPackages.SourceCommit);

			await AssertTheNewObjectIsLiveInTheMasterRoom(applied.CreatedObjects["wiki_global"]);
		}
		finally
		{
			await factory.Services.GetRequiredService<IPackageInstallService>()
				.UninstallAsync("wiki-reader", force: true, CancellationToken.None);
		}
	}

	/// <summary>
	/// The two ways an installed package used to arrive dead, both asserted against the live game
	/// rather than the registry, because the registry recorded success in both cases.
	/// <para>
	/// First, the master room's <c>contents</c> was cached and the installer created its object by
	/// calling the store directly, so nothing invalidated it — and command matching walks exactly
	/// that list (<c>SharpMUSHParserVisitor</c> step 15). <c>+wiki</c> answered "Huh?" until the
	/// entry expired or the game restarted.
	/// </para>
	/// <para>
	/// Second, going back through <c>CreateThingCommand</c> to get that invalidation brought the
	/// configured default thing flags with it, and the stock <c>thing_flags</c> is
	/// <c>no_command</c> — PennMUSH's own default, and the one flag that silently disables every
	/// <c>$</c>-command a package ships. A package object's flags come from its manifest.
	/// </para>
	/// </summary>
	private async Task AssertTheNewObjectIsLiveInTheMasterRoom(string objid)
	{
		var created = PackageInstallService.ParseObjid(objid)!.Value;

		await Assert.That(await MasterRoomContentsAsync()).Contains(created.Number);

		var node = (await factory.Services.GetRequiredService<IMediator>()
			.Send(new GetObjectNodeQuery(created))).Known();
		await Assert.That(await node.HasFlag("NO_COMMAND")).IsFalse();
	}

	private async Task<List<int>> MasterRoomContentsAsync()
	{
		var masterRoom = new DBRef(Convert.ToInt32(factory.Services
			.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.MasterRoom));

		return await factory.Services.GetRequiredService<IMediator>()
			.CreateStream(new GetContentsQuery(masterRoom))
			.Select(x => x.Object().DBRef.Number)
			.ToListAsync();
	}
}
