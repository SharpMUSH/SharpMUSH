using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The precedence ladder <c>IAttributeStore.GetAttributeWithInheritanceAsync</c> documents, exercised
/// against the LMDB provider: object, then the whole parent chain, then the object's zones, then each
/// parent's zones in chain order — with <c>no_inherit</c> on any existing prefix of a candidate
/// aborting the walk instead of falling through (PennMUSH <c>atr_get_with_parent</c>).
/// </summary>
public class AttributeInheritanceTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public Task Cleanup()
	{
		_db.Store.Dispose();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}

		return Task.CompletedTask;
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;

	private async Task<DBRef> Thing(string name)
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
		return await _db.CreateThingAsync(name, room, await God(), room);
	}

	private async Task<AnySharpObject> Node(DBRef dbref) => (await _db.GetObjectNodeAsync(dbref)).Known;

	private async Task Parent(DBRef child, DBRef parent)
		=> await _db.SetObjectParent(await Node(child), await Node(parent));

	private async Task Zone(DBRef obj, DBRef zone)
		=> await _db.SetObjectZone(await Node(obj), await Node(zone));

	private async Task Set(DBRef target, string[] path, string value)
		=> await _db.SetAttributeAsync(target, path, MModule.single(value), await God());

	[Test]
	public async Task SelfBeatsParent()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		await Parent(child, parent);
		await Set(parent, ["GREET"], "from parent");
		await Set(child, ["GREET"], "from self");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"]).SingleAsync();

		await Assert.That(found.Source).IsEqualTo(AttributeSource.Self);
		await Assert.That(found.SourceObject.Number).IsEqualTo(child.Number);
		await Assert.That(MModule.plainText(found.Attributes[^1].Value)).IsEqualTo("from self");
	}

	[Test]
	public async Task ParentBeatsTheObjectsZone()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var zone = await Thing("Zone");
		await Parent(child, parent);
		await Zone(child, zone);
		await Set(parent, ["GREET"], "from parent");
		await Set(zone, ["GREET"], "from zone");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"]).SingleAsync();

		await Assert.That(found.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(found.SourceObject.Number).IsEqualTo(parent.Number);
		await Assert.That(MModule.plainText(found.Attributes[^1].Value)).IsEqualTo("from parent");
	}

	[Test]
	public async Task GrandparentIsReachedThroughTwoHops()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var grandparent = await Thing("Grandparent");
		await Parent(child, parent);
		await Parent(parent, grandparent);
		await Set(grandparent, ["FOO", "BAR"], "deep");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["FOO", "BAR"]).SingleAsync();

		await Assert.That(found.Source).IsEqualTo(AttributeSource.Parent);
		await Assert.That(found.SourceObject.Number).IsEqualTo(grandparent.Number);
		await Assert.That(found.Attributes.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO", "FOO`BAR" });
	}

	[Test]
	public async Task TheParentsZoneIsConsultedAfterTheObjectsOwnZone()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var ownZone = await Thing("OwnZone");
		var parentZone = await Thing("ParentZone");
		await Parent(child, parent);
		await Zone(child, ownZone);
		await Zone(parent, parentZone);
		await Set(ownZone, ["GREET"], "own zone");
		await Set(parentZone, ["GREET"], "parent zone");

		var first = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"]).SingleAsync();
		await Assert.That(first.Source).IsEqualTo(AttributeSource.Zone);
		await Assert.That(first.SourceObject.Number).IsEqualTo(ownZone.Number);

		await _db.ClearAttributeAsync(ownZone, ["GREET"]);

		var second = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"]).SingleAsync();
		await Assert.That(second.Source).IsEqualTo(AttributeSource.Zone);
		await Assert.That(second.SourceObject.Number).IsEqualTo(parentZone.Number);
	}

	[Test]
	public async Task NoInheritOnTheParentsPrefixStopsTheWalk()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var grandparent = await Thing("Grandparent");
		await Parent(child, parent);
		await Parent(parent, grandparent);
		await Set(grandparent, ["FOO", "BAR"], "deep");
		// The parent carries only the branch prefix, flagged no_inherit: Penn returns NULL rather
		// than falling through to the grandparent that does have the leaf.
		await Set(parent, ["FOO"], "prefix only");
		var noInherit = await _db.GetAttributeFlagAsync("no_inherit");
		await _db.SetAttributeFlagAsync((await Node(parent)).Object(), ["FOO"], noInherit!);

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["FOO", "BAR"]).ToListAsync();

		await Assert.That(found).IsEmpty();
	}

	[Test]
	public async Task InheritedFlagsDropTheNonInheritableOnes()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		await Parent(child, parent);
		await Set(parent, ["GREET"], "from parent");
		var visual = await _db.GetAttributeFlagAsync("visual");
		await _db.SetAttributeFlagAsync((await Node(parent)).Object(), ["GREET"], visual!);

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"]).SingleAsync();

		await Assert.That(found.Attributes[^1].Flags.Select(f => f.Name)).Contains("visual");
		await Assert.That(found.InheritedFlags.Select(f => f.Name)).DoesNotContain("visual");
	}

	[Test]
	public async Task AParentCycleTerminates()
	{
		var a = await Thing("A");
		var b = await Thing("B");
		await Parent(a, b);
		await Parent(b, a);
		await Set(b, ["GREET"], "from b");

		var found = await _db.GetAttributeWithInheritanceAsync(a, ["GREET"]).SingleAsync();
		await Assert.That(found.SourceObject.Number).IsEqualTo(b.Number);

		var missing = await _db.GetAttributeWithInheritanceAsync(a, ["NOPE"]).ToListAsync();
		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task CheckParentFalseStopsAtTheObjectItself()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		await Parent(child, parent);
		await Set(parent, ["GREET"], "from parent");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"], checkParent: false).ToListAsync();
		await Assert.That(found).IsEmpty();
	}

	[Test]
	public async Task TheLazyWalkResolvesThroughTheSameLadder()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var zone = await Thing("Zone");
		await Parent(child, parent);
		await Zone(child, zone);
		await Set(zone, ["GREET"], "from zone");

		var found = await _db.GetLazyAttributeWithInheritanceAsync(child, ["GREET"]).SingleAsync();

		await Assert.That(found.Source).IsEqualTo(AttributeSource.Zone);
		await Assert.That(found.SourceObject.Number).IsEqualTo(zone.Number);
		await Assert.That(MModule.plainText(await found.Attributes[^1].Value.WithCancellation(CancellationToken.None)))
			.IsEqualTo("from zone");
	}
}
