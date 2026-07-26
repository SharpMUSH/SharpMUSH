using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// The <c>room-contents</c> bundled package is what makes the portal's Play sidebar work: it
/// attaches the <c>ROOM`CONTENTS</c> event handler that pushes <c>room.contents</c> /
/// <c>room.exits</c> OOB frames to a room's connected occupants. It previously shipped as
/// documentation only — nothing installed it — so a stock server emitted no OOB at all and the
/// Here/Exits cards were permanently empty.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class RoomContentsPackageTests(ServerWebAppFactory factory)
{
	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task IsInstalledAtBoot_AsAnAttachPackageManagingTheHandlerAttributes()
	{
		var installed = await Registry.GetInstalledPackageAsync("room-contents");
		await Assert.That(installed.IsT0).IsTrue();

		// Attach mode: it manages attributes on the pre-seeded event_handler object and creates
		// no objects of its own, so uninstalling leaves that object's other softcode intact.
		await Assert.That((await Registry.GetPackageObjectsAsync("room-contents")).Count).IsEqualTo(0);

		var managed = (await Registry.GetManagedAttributesAsync("room-contents"))
			.Select(m => m.Attribute)
			.ToList();

		await Assert.That(managed).Contains("ROOM`CONTENTS");
		await Assert.That(managed).Contains("FN`WHOROW");
		await Assert.That(managed).Contains("FN`EXITROW");
		await Assert.That(managed).Contains("FN`WHOVIS");
	}

	/// <summary>
	/// The managed attributes must land on the configured event_handler object — that is the only
	/// object <see cref="IEventService"/> reads handler attributes from. Asserted against the
	/// configured dbref rather than a literal, because the seed numbering is config-driven and has
	/// moved before.
	/// </summary>
	[Test]
	public async Task ManagedAttributes_LandOnTheConfiguredEventHandler()
	{
		var configured = factory.Services
			.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
			.CurrentValue.Database.EventHandler;

		var managed = await Registry.GetManagedAttributesAsync("room-contents");
		var handler = managed.Single(m => m.Attribute == "ROOM`CONTENTS");

		await Assert.That(handler.Objid).StartsWith($"#{configured}:");
	}
}
