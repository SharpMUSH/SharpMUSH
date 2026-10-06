using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Object-held roles and overrides in the Lightning provider: the role-backed flags and powers land
/// there whoever writes them, the delete cascade clears them, and the migration moves edges a world
/// stored as flags and powers.
/// </summary>
public class ObjectGrantsStoreTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;

	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: LMDB's lock file can outlive the writer thread by a few milliseconds.
				// A leftover temp directory costs disk, not correctness.
			}
		}
	}

	private async Task<AnySharpObject> ThingAsync(string name)
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Expect<AnySharpObject>().AsContainer;
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var dbref = await _db.CreateThingAsync(name, room, god, room);
		return (await _db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();
	}

	[Test]
	public async Task SeedGivesTheSeededWizardsTheWizardRole()
	{
		await Assert.That(await _db.GetObjectRolesAsync(1)).Contains(BuiltInRoles.WizardSlug);
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>();
		await Assert.That(await god.Object().Flags.Value.AnyAsync(f => f.Name == "WIZARD")).IsFalse();
		await Assert.That((await god.Object().Grants.WithCancellation(CancellationToken.None)).Has(PortalPermission.GameWizard)).IsTrue();
	}

	[Test]
	public async Task RoleFlagIsWrittenAsAnObjectRole()
	{
		var thing = await ThingAsync("Flagged");
		var wizard = (await _db.GetObjectFlagAsync("WIZARD"))!;
		await Assert.That(await _db.SetObjectFlagAsync(thing, wizard)).IsTrue();
		await Assert.That(await _db.GetObjectRolesAsync(thing.Object().Key)).IsEquivalentTo([BuiltInRoles.WizardSlug]);
		await Assert.That(await thing.Object().Flags.Value.AnyAsync(f => f.Name == "WIZARD")).IsFalse();
		await Assert.That((await thing.Object().Grants.WithCancellation(CancellationToken.None)).Has(PortalPermission.GameWizard)).IsTrue();

		await Assert.That(await _db.UnsetObjectFlagAsync(thing, wizard)).IsTrue();
		await Assert.That(await _db.GetObjectRolesAsync(thing.Object().Key)).IsEmpty();
	}

	[Test]
	public async Task PowerIsWrittenAsAnOverrideAndBuilderAsARole()
	{
		var thing = await ThingAsync("Powered");
		var seeAll = (await _db.GetPowerAsync("See_All"))!;
		var builder = (await _db.GetPowerAsync("Builder"))!;
		await _db.SetObjectPowerAsync(thing, seeAll);
		await _db.SetObjectPowerAsync(thing, builder);
		var number = thing.Object().Key;
		await Assert.That((await _db.GetObjectOverridesAsync(number))[PortalPermission.GamePower("See_All")]).IsEqualTo(PermissionState.Allow);
		await Assert.That(await _db.GetObjectRolesAsync(number)).IsEquivalentTo([BuiltInRoles.BuilderSlug]);
		await Assert.That(await thing.Object().Powers.Value.CountAsync()).IsEqualTo(0);

		await _db.UnsetObjectPowerAsync(thing, seeAll);
		await _db.UnsetObjectPowerAsync(thing, builder);
		await Assert.That(await _db.GetObjectOverridesAsync(number)).IsEmpty();
		await Assert.That(await _db.GetObjectRolesAsync(number)).IsEmpty();
	}

	[Test]
	public async Task DeletingAnObjectClearsItsRolesAndOverrides()
	{
		var thing = await ThingAsync("Doomed");
		var number = thing.Object().Key;
		await _db.AssignRoleToObjectAsync(number, "helper");
		await _db.SetObjectOverrideAsync(number, PortalPermission.WikiEdit, PermissionState.Deny);
		await Assert.That(await _db.GetObjectsForRoleAsync("helper")).Contains(number);

		await _db.DeleteObjectAsync(thing.Object().DBRef);

		await Assert.That(await _db.GetObjectRolesAsync(number)).IsEmpty();
		await Assert.That(await _db.GetObjectOverridesAsync(number)).IsEmpty();
		await Assert.That(await _db.GetObjectsForRoleAsync("helper")).DoesNotContain(number);
	}
}
