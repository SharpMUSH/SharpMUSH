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
}
