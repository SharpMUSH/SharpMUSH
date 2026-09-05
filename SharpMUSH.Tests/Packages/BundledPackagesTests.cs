using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Guards the bundled-package wiring: every entry in <see cref="BundledPackages.All"/> must have
/// its manifest embedded in the server assembly and parse cleanly, because a missing
/// EmbeddedResource or a manifest typo only surfaces at first boot (as softcode that silently
/// never installs).
/// </summary>
public class BundledPackagesTests
{
	private readonly PackageManifestService _manifests = new();

	[Test]
	public async Task EveryBundledPackage_HasAnEmbeddedManifestThatParses()
	{
		foreach (var descriptor in BundledPackages.All)
		{
			var yaml = BundledPackages.ManifestYaml(descriptor.PackageId);
			var parsed = _manifests.ParseManifest(yaml);

			await Assert.That(parsed.IsT0)
				.IsTrue()
				.Because($"bundled manifest '{descriptor.PackageId}' must parse");
			await Assert.That(parsed.AsT0.Manifest.Name).IsEqualTo(descriptor.PackageId);
		}
	}

	/// <summary>
	/// room-contents is the softcode behind the portal's Play sidebar: it attaches the
	/// ROOM`CONTENTS handler to the configured event_handler object. Before it was bundled the
	/// handler shipped as documentation only, so a stock server emitted no room.contents /
	/// room.exits pushes at all and the sidebar had no data source.
	/// </summary>
	[Test]
	public async Task RoomContents_AttachesTheRoomContentsHandlerToTheEventHandler()
	{
		var parsed = _manifests.ParseManifest(BundledPackages.ManifestYaml("room-contents"));
		await Assert.That(parsed.IsT0).IsTrue();

		var handler = parsed.AsT0.Manifest.Objects.Single();
		await Assert.That(handler.Target).IsEqualTo(new PackageRef(PackageRefKind.WellKnown, "event_handler"));
		await Assert.That(handler.Attributes.Keys).Contains("ROOM`CONTENTS");
	}

	/// <summary>
	/// An attach-mode bundled package must declare the handler it attaches to, or bootstrap
	/// would try to install it on a game that has no target for its {{$...}} target ref.
	/// </summary>
	[Test]
	public async Task AttachModePackages_DeclareTheHandlerTheyTarget()
	{
		foreach (var descriptor in BundledPackages.All)
		{
			// Assert the parse rather than assuming it: reaching AsT0 on a failed parse throws an
			// opaque OneOf exception, and this test would silently depend on running after
			// EveryBundledPackage_HasAnEmbeddedManifestThatParses to get a readable failure.
			var parsed = _manifests.ParseManifest(BundledPackages.ManifestYaml(descriptor.PackageId));
			await Assert.That(parsed.IsT0)
				.IsTrue()
				.Because($"bundled manifest '{descriptor.PackageId}' must parse");

			var manifest = parsed.AsT0.Manifest;

			var expected = descriptor.Requires switch
			{
				BundledPackageHandler.Http => WellKnownRefs.HttpHandler,
				BundledPackageHandler.Event => WellKnownRefs.EventHandler,
				_ => null
			};

			var wellKnownTargets = manifest.Objects
				.Where(o => o.Target is { Kind: PackageRefKind.WellKnown })
				.Select(o => o.Target!.Name)
				.ToList();

			if (expected is null)
			{
				await Assert.That(wellKnownTargets)
					.DoesNotContain(WellKnownRefs.HttpHandler)
					.And.DoesNotContain(WellKnownRefs.EventHandler);
			}
			else
			{
				await Assert.That(wellKnownTargets)
					.Contains(expected)
					.Because($"{descriptor.PackageId} is declared as attaching to {expected}");
			}
		}
	}

	/// <summary>
	/// Shipping a package in the image and installing it into every game are separate decisions.
	/// This pins the set that first boot creates: a package added to the catalogue must opt in
	/// explicitly, so the next application to ship cannot appear in every game by accident.
	/// </summary>
	[Test]
	public async Task FirstBootInstalls_OnlyTheFlaggedDefaults()
	{
		var installed = BundledPackages.All
			.Where(d => d.InstallAtFirstBoot)
			.Select(d => d.PackageId);

		await Assert.That(installed).IsEquivalentTo(new[]
		{
			"http-handler", "profile-handler", "room-contents", "common-functions", "plus-help", "scene"
		});
	}

	/// <summary>
	/// The list is walked in order by the offline catalogue and by first-boot install, so a
	/// dependency has to precede its dependents. plus-help is the first bundled package another
	/// bundled package depends on — scene and wiki-reader attach their <c>SRC</c> help registration
	/// to its librarian, which does not exist until plus-help is installed.
	/// </summary>
	[Test]
	public async Task ADependencyPrecedesItsDependents()
	{
		var order = BundledPackages.All.Select(d => d.PackageId).ToList();

		await Assert.That(order.IndexOf("plus-help")).IsLessThan(order.IndexOf("scene"));
		await Assert.That(order.IndexOf("plus-help")).IsLessThan(order.IndexOf("wiki-reader"));
		await Assert.That(order.IndexOf("common-functions")).IsLessThan(order.IndexOf("plus-help"));
	}

	/// <summary>
	/// wiki-reader is the first available-not-installed package: it ships in the image so an admin
	/// can install it offline, but it puts a +wiki object in the master room, which no game should
	/// get without asking for it.
	/// </summary>
	[Test]
	public async Task WikiReader_ShipsInTheCatalogue_ButNotAtFirstBoot()
	{
		await Assert.That(BundledPackages.All.Select(d => d.PackageId)).Contains("wiki-reader");

		var descriptor = BundledPackages.All.Single(d => d.PackageId == "wiki-reader");
		await Assert.That(descriptor.InstallAtFirstBoot).IsFalse();
		await Assert.That(descriptor.Requires).IsEqualTo(BundledPackageHandler.None);

		var parsed = _manifests.ParseManifest(BundledPackages.ManifestYaml("wiki-reader"));
		await Assert.That(parsed.IsT0).IsTrue().Because("wiki-reader's manifest must be embedded");
	}

	/// <summary>
	/// A catalogue package must be installable from the catalogue alone. An entry whose dependency
	/// only exists in the git repo would offer an offline install that then needs the network —
	/// the one failure the bundled source exists to avoid.
	/// </summary>
	[Test]
	public async Task EveryCatalogueDependency_IsAlsoInTheCatalogue()
	{
		var catalogue = BundledPackages.All.Select(d => d.PackageId).ToHashSet();

		foreach (var descriptor in BundledPackages.All)
		{
			var parsed = _manifests.ParseManifest(BundledPackages.ManifestYaml(descriptor.PackageId));
			await Assert.That(parsed.IsT0)
				.IsTrue()
				.Because($"bundled manifest '{descriptor.PackageId}' must parse");

			foreach (var dependency in parsed.AsT0.Manifest.Dependencies)
			{
				await Assert.That(catalogue)
					.Contains(dependency.PackageId)
					.Because($"{descriptor.PackageId} depends on {dependency.PackageId}, which must ship too");
			}
		}
	}
}
