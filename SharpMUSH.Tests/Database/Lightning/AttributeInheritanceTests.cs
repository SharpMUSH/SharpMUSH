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
/// The walk <c>IAttributeStore.GetAttributeWithInheritanceAsync</c> documents — PennMUSH's
/// <c>atr_get_with_parent</c> (<c>src/attrib.c:1203-1278</c>) — exercised against the LMDB provider:
/// the object, its parent chain, then the type ancestor and its parents, never a zone; <c>no_inherit</c>
/// on any existing prefix of a candidate ends the whole lookup; the depth bound is <c>MAX_PARENTS</c>
/// objects per leg; and a name found nowhere is retried as the standard attribute it means.
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
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<DBRef> Thing(string name)
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Expect<AnySharpObject>().AsOptionalContainer.Expect<AnySharpContainer>();
		return await _db.CreateThingAsync(name, room, await God(), room);
	}

	private async Task<AnySharpObject> Node(DBRef dbref) => (await _db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private async Task Parent(DBRef child, DBRef parent)
		=> await _db.SetObjectParent(await Node(child), await Node(parent));

	private async Task Zone(DBRef obj, DBRef zone)
		=> await _db.SetObjectZone(await Node(obj), await Node(zone));

	private async Task Set(DBRef target, string[] path, string value)
		=> await _db.SetAttributeAsync(target, path, MarkupText.Plain(value), await God());

	private async Task NoInherit(DBRef target, string[] path)
		=> await _db.SetAttributeFlagAsync((await Node(target)).Object(), path, (await _db.GetAttributeFlagAsync("no_inherit"))!);

	private static InheritanceWalk WithAncestor(DBRef ancestor) => new(ancestor, InheritanceWalk.DefaultMaxParents);

	/// <summary>A chain <paramref name="length"/> parents long above a new child: the child first.</summary>
	private async Task<DBRef[]> Chain(int length)
	{
		var chain = new List<DBRef> { await Thing("Child") };
		for (var i = 1; i <= length; i++)
		{
			var next = await Thing($"Parent{i}");
			await Parent(chain[^1], next);
			chain.Add(next);
		}

		return [.. chain];
	}

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
		await Assert.That(found.Attributes[^1].Value.ToPlainText()).IsEqualTo("from self");
	}

	/// <summary>
	/// <c>atr_get_with_parent</c> never looks at <c>Zone()</c> (<c>src/attrib.c:1203-1278</c>); a zone
	/// supplies $-commands only. Neither the object's own zone nor a parent's zone is read.
	/// </summary>
	[Test]
	public async Task NoZoneIsEverConsulted()
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

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"]).ToListAsync();

		await Assert.That(found).IsEmpty();
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

	/// <summary>
	/// The ancestor comes after the whole parent chain, and its own parents after it
	/// (<c>src/attrib.c:1262-1269</c>).
	/// </summary>
	[Test]
	public async Task TheAncestorAndItsParentsFollowTheChain()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var ancestor = await Thing("Ancestor");
		var ancestorParent = await Thing("AncestorParent");
		await Parent(child, parent);
		await Parent(ancestor, ancestorParent);
		await Set(ancestor, ["GREET"], "from the ancestor");
		await Set(ancestorParent, ["GREET"], "from the ancestor's parent");
		await Set(ancestorParent, ["DEEP"], "from the ancestor's parent");

		var greet = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"], walk: WithAncestor(ancestor)).SingleAsync();
		var deep = await _db.GetAttributeWithInheritanceAsync(child, ["DEEP"], walk: WithAncestor(ancestor)).SingleAsync();

		await Assert.That(greet.Source).IsEqualTo(AttributeSource.Ancestor);
		await Assert.That(greet.SourceObject.Number).IsEqualTo(ancestor.Number);
		await Assert.That(deep.Source).IsEqualTo(AttributeSource.Ancestor);
		await Assert.That(deep.SourceObject.Number).IsEqualTo(ancestorParent.Number);

		await Set(parent, ["GREET"], "from the parent");
		var shadowed = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"], walk: WithAncestor(ancestor)).SingleAsync();
		await Assert.That(shadowed.SourceObject.Number).IsEqualTo(parent.Number);
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
		await NoInherit(parent, ["FOO"]);

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["FOO", "BAR"]).ToListAsync();

		await Assert.That(found).IsEmpty();
	}

	/// <summary>
	/// A no_inherit hit on a parent is <c>return NULL</c> out of <c>atr_get_with_parent</c>
	/// (<c>src/attrib.c:1250-1251</c>): the type ancestor's copy is never reached.
	/// </summary>
	[Test]
	public async Task NoInheritOnAParentHidesTheAncestorsCopy()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var ancestor = await Thing("Ancestor");
		await Parent(child, parent);
		await Set(parent, ["FOO"], "parent");
		await NoInherit(parent, ["FOO"]);
		await Set(ancestor, ["FOO"], "ancestor");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["FOO"], walk: WithAncestor(ancestor)).ToListAsync();

		await Assert.That(found).IsEmpty();
	}

	/// <summary>
	/// The same barrier stops the alias retry too: the <c>return NULL</c> leaves the function, and the
	/// retry is the outer <c>for (;;)</c> (<c>src/attrib.c:1216-1277</c>).
	/// </summary>
	[Test]
	public async Task NoInheritOnAParentStopsTheAliasRetry()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var grandparent = await Thing("Grandparent");
		await Parent(child, parent);
		await Parent(parent, grandparent);
		await Set(parent, ["DESC"], "private alias-named copy");
		await NoInherit(parent, ["DESC"]);
		await Set(grandparent, ["DESCRIBE"], "the real description");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["DESC"]).ToListAsync();

		await Assert.That(found).IsEmpty();
	}

	/// <summary>
	/// <c>atr_match</c> (<c>src/atr_tab.c:112-122</c>): a name found nowhere is retried under the
	/// standard attribute it means — an <c>attralias</c> name, or a unique prefix of a
	/// <c>prefixmatch</c> attribute. An ambiguous prefix means nothing.
	/// </summary>
	[Test]
	[Arguments("DESC")]
	[Arguments("DESCR")]
	[Arguments("descri")]
	public async Task AnAliasOrUniquePrefixReadsTheStandardAttribute(string name)
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		await Parent(child, parent);
		await Set(parent, ["DESCRIBE"], "described");

		var found = await _db.GetAttributeWithInheritanceAsync(child, [name]).SingleAsync();
		var own = await _db.GetAttributeWithInheritanceAsync(parent, [name], checkParent: false).SingleAsync();

		await Assert.That(found.Attributes[^1].LongName).IsEqualTo("DESCRIBE");
		await Assert.That(found.SourceObject.Number).IsEqualTo(parent.Number);
		await Assert.That(own.Attributes[^1].LongName).IsEqualTo("DESCRIBE");
	}

	[Test]
	public async Task AnAmbiguousPrefixIsNotRetried()
	{
		var child = await Thing("Child");
		await Set(child, ["DESCRIBE"], "described");

		// DES begins DESC (an alias), DESCFORMAT and DESCRIBE.
		var found = await _db.GetAttributeWithInheritanceAsync(child, ["DES"]).ToListAsync();

		await Assert.That(found).IsEmpty();
	}

	/// <summary>
	/// <c>while (parent_depth &lt; MAX_PARENTS ...)</c> (<c>src/attrib.c:1222</c>) visits the object and
	/// <c>MAX_PARENTS - 1</c> parents; when the chain is still going there, the ancestor is never reached.
	/// </summary>
	[Test]
	public async Task TheWalkVisitsTheObjectAndMaxParentsMinusOneParents()
	{
		const int maxParents = 4;
		var chain = await Chain(maxParents);
		var ancestor = await Thing("Ancestor");
		var walk = new InheritanceWalk(ancestor, maxParents);
		await Set(chain[maxParents - 1], ["REACHED"], "last visited parent");
		await Set(chain[maxParents], ["BEYOND"], "one parent too far");
		await Set(ancestor, ["ANCESTRAL"], "ancestor");

		var reached = await _db.GetAttributeWithInheritanceAsync(chain[0], ["REACHED"], walk: walk).SingleAsync();
		var beyond = await _db.GetAttributeWithInheritanceAsync(chain[0], ["BEYOND"], walk: walk).ToListAsync();
		var ancestral = await _db.GetAttributeWithInheritanceAsync(chain[0], ["ANCESTRAL"], walk: walk).ToListAsync();

		await Assert.That(reached.SourceObject.Number).IsEqualTo(chain[maxParents - 1].Number);
		await Assert.That(beyond).IsEmpty();
		await Assert.That(ancestral).IsEmpty();
	}

	/// <summary>
	/// A chain that ends inside the bound resets the depth for the ancestor's leg
	/// (<c>parent_depth = 0; target = ancestor</c>, <c>src/attrib.c:1265-1268</c>).
	/// </summary>
	[Test]
	public async Task AChainEndingInsideTheBoundReachesTheAncestorsWholeLeg()
	{
		const int maxParents = 4;
		var chain = await Chain(maxParents - 1);
		var ancestorChain = await Chain(maxParents);
		var walk = new InheritanceWalk(ancestorChain[0], maxParents);
		await Set(ancestorChain[maxParents - 1], ["FAR"], "the ancestor's last visited parent");
		await Set(ancestorChain[maxParents], ["TOOFAR"], "beyond the ancestor's leg");

		var far = await _db.GetAttributeWithInheritanceAsync(chain[0], ["FAR"], walk: walk).SingleAsync();
		var tooFar = await _db.GetAttributeWithInheritanceAsync(chain[0], ["TOOFAR"], walk: walk).ToListAsync();

		await Assert.That(far.Source).IsEqualTo(AttributeSource.Ancestor);
		await Assert.That(far.SourceObject.Number).IsEqualTo(ancestorChain[maxParents - 1].Number);
		await Assert.That(tooFar).IsEmpty();
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
		var ancestor = await Thing("Ancestor");
		await Parent(child, parent);
		await Set(parent, ["GREET"], "from parent");
		await Set(ancestor, ["GREET"], "from ancestor");

		var found = await _db.GetAttributeWithInheritanceAsync(child, ["GREET"], checkParent: false,
			walk: WithAncestor(ancestor)).ToListAsync();
		await Assert.That(found).IsEmpty();
	}

	[Test]
	public async Task TheLazyWalkResolvesThroughTheSameLadder()
	{
		var child = await Thing("Child");
		var parent = await Thing("Parent");
		var ancestor = await Thing("Ancestor");
		await Parent(child, parent);
		await Set(ancestor, ["GREET"], "from ancestor");

		var found = await _db.GetLazyAttributeWithInheritanceAsync(child, ["GREET"], walk: WithAncestor(ancestor)).SingleAsync();

		await Assert.That(found.Source).IsEqualTo(AttributeSource.Ancestor);
		await Assert.That(found.SourceObject.Number).IsEqualTo(ancestor.Number);
		await Assert.That((await found.Attributes[^1].Value.WithCancellation(CancellationToken.None)).ToPlainText())
			.IsEqualTo("from ancestor");
	}
}
