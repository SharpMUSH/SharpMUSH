using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A real PennMUSH 1.8.8 dump written by a game that had destroyed #7, #8 and #10, so the dump has
/// holes where PennMUSH kept garbage and <c>~15</c> as its <c>db_top</c>. Every object arrives under
/// the dbref it had, because the softcode, locks and mail in the same dump name objects by number.
/// </summary>
/// <remarks>
/// The fixture's objects: #0 Room Zero, #1 One, #2 Master Room, #3 Alice, #4 Bob, #5 Carol, #6 Widget,
/// #9 Gadget, #11 Oracle Hall, #12 Attic, #13 Hall (exit), #14 North (exit). Widget's attributes point
/// forward at #9 and #11; Gadget's point back at #6; Attic's mention the missing #7, #8 and #10.
/// </remarks>
public class PennMUSHDbrefPreservationTests
{
	internal static readonly string FixturePath =
		Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0-holes.outdb");

	[Test]
	[Arguments("Alice", 3)]
	[Arguments("Bob", 4)]
	[Arguments("Carol", 5)]
	[Arguments("Widget", 6)]
	[Arguments("Gadget", 9)]
	[Arguments("Oracle Hall", 11)]
	[Arguments("Attic", 12)]
	[Arguments("Hall", 13)]
	[Arguments("North", 14)]
	public async Task EveryObjectKeepsItsSourceDbref(string name, int expected)
	{
		await using var world = await ImportAsync();

		var matches = await world.Database.GetAllObjectsAsync().Where(o => o.Name == name).ToListAsync();

		await Assert.That(matches.Select(o => o.Key).ToList()).IsEquivalentTo([expected]);
	}

	[Test]
	public async Task TheHolesStayEmptyAndNothingElseIsThere()
	{
		await using var world = await ImportAsync();

		var keys = await world.Database.GetAllObjectsAsync().Select(o => o.Key).OrderBy(k => k).ToListAsync();

		await Assert.That(keys).IsEquivalentTo([0, 1, 2, 3, 4, 5, 6, 9, 11, 12, 13, 14]);
		foreach (var hole in (int[])[7, 8, 10])
		{
			await Assert.That((await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(hole)))).IsNone).IsTrue();
		}
	}

	/// <summary>
	/// The counter follows the import to one past the highest id, so the next @create cannot land on an
	/// object the import wrote, and the holes are not silently refilled by the import's own writes.
	/// </summary>
	[Test]
	public async Task NewObjectsAreAllocatedAfterTheHighestImportedDbref()
	{
		await using var world = await ImportAsync();
		var god = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var limbo = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<SharpRoom>();

		var created = await world.Database.CreateThingAsync("After Import", limbo, god, limbo);

		await Assert.That(created.Number).IsEqualTo(15);
	}

	[Test]
	public async Task OwnersParentsZonesLocationsAndExitsPointAtTheSameNumbers()
	{
		await using var world = await ImportAsync();
		var widget = await NodeAsync(world, 6);
		var hall = await NodeAsync(world, 11);

		await Assert.That((await widget.Object().Owner.WithCancellation(CancellationToken.None)).Object.Key).IsEqualTo(1);
		await Assert.That((await widget.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key).IsEqualTo(9);
		await Assert.That((await widget.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key).IsEqualTo(9);
		await Assert.That((await widget.Expect<SharpThing>().Location.WithCancellation(CancellationToken.None)).Object().Key).IsEqualTo(11);
		await Assert.That((await widget.Expect<SharpThing>().Home.WithCancellation(CancellationToken.None)).Object().Key).IsEqualTo(11);
		// Oracle Hall's PennMUSH location (its drop-to) is #-1.
		await Assert.That((await hall.Expect<SharpRoom>().Location.WithCancellation(CancellationToken.None)).IsNone).IsTrue();

		// A PennMUSH exit's `exits` field is its source and its `location` its destination: both exits
		// leave Room Zero, Hall for #11 and North for #12.
		var hallExit = (await NodeAsync(world, 13)).Expect<SharpExit>();
		await Assert.That((await hallExit.Location.WithCancellation(CancellationToken.None)).Object().Key).IsEqualTo(0);
		await Assert.That((await hallExit.Home.WithCancellation(CancellationToken.None)).Object()!.Key).IsEqualTo(11);
		var north = (await NodeAsync(world, 14)).Expect<SharpExit>();
		await Assert.That((await north.Location.WithCancellation(CancellationToken.None)).Object().Key).IsEqualTo(0);
		await Assert.That((await north.Home.WithCancellation(CancellationToken.None)).Object()!.Key).IsEqualTo(12);
		var alice = await NodeAsync(world, 3);
		await Assert.That((await alice.Object().Owner.WithCancellation(CancellationToken.None)).Object.Key).IsEqualTo(3);
	}

	/// <summary>
	/// Softcode names objects by number, forward and back and across the holes, and the number written
	/// in the attribute is the number the object has: nothing in the text is rewritten because nothing
	/// needed to be.
	/// </summary>
	[Test]
	public async Task EmbeddedDbrefsInAttributeTextResolveToTheObjectsTheyNamed()
	{
		await using var world = await ImportAsync();

		await Assert.That(await AttributeAsync(world, 6, "NOTE"))
			.IsEqualTo("Gadget is #9 and the hall is #11; owner=[owner(#6)] parent is [parent(#6)]");
		await Assert.That(await AttributeAsync(world, 9, "BACK"))
			.IsEqualTo("Widget lives at #6 and Alice at #3; %#[num(me)]");
		await Assert.That(await AttributeAsync(world, 6, "CMD")).IsEqualTo("$poke *:@pemit %#=You poke #9 with %0");
		await Assert.That(await AttributeAsync(world, 12, "FWD"))
			.IsEqualTo("Points at #11 and at #9 (after the gap at #7 #8 #10)");
		await Assert.That(await AttributeAsync(world, 11, "DESCRIBE")).IsEqualTo("The hall of #6 and #9.");

		// The numbers those attributes hold still name the objects they named in the source.
		await Assert.That((await NodeAsync(world, 9)).Object().Name).IsEqualTo("Gadget");
		await Assert.That((await NodeAsync(world, 11)).Object().Name).IsEqualTo("Oracle Hall");
		await Assert.That((await NodeAsync(world, 6)).Object().Name).IsEqualTo("Widget");
		await Assert.That((await NodeAsync(world, 3)).Object().Name).IsEqualTo("Alice");
	}

	[Test]
	public async Task LockKeysKeepTheirDbrefs()
	{
		await using var world = await ImportAsync();
		var widget = await NodeAsync(world, 6);
		var gadget = await NodeAsync(world, 9);

		await Assert.That(widget.Object().Locks["Basic"].LockString).IsEqualTo("#9");
		await Assert.That(widget.Object().Locks["Use"].LockString).IsEqualTo("#4|#11");
		await Assert.That(gadget.Object().Locks["Enter"].LockString).IsEqualTo("#6");
	}

	/// <summary>The import's own summary counts what came in and does not report the holes as failures.</summary>
	[Test]
	public async Task TheImportSummaryCountsWhatArrived()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(await world.Parser.ParseFileAsync(FixturePath));

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.TotalObjects).IsEqualTo(12);
		await Assert.That(result.PlayersConverted).IsEqualTo(4);
		await Assert.That(result.RoomsConverted).IsEqualTo(4);
		await Assert.That(result.ThingsConverted).IsEqualTo(2);
		await Assert.That(result.ExitsConverted).IsEqualTo(2);
	}

	/// <summary>
	/// A world that already holds an object at a number the source uses is not silently remapped: the
	/// object that cannot take its number is reported, and nothing else moves.
	/// </summary>
	[Test]
	public async Task ANumberAlreadyTakenIsReportedNotRemapped()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var god = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var limbo = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<SharpRoom>();
		var squatter = await world.Database.CreateThingAsync("Squatter", limbo, god, limbo, requestedDbref: 11);
		await Assert.That(squatter.Number).IsEqualTo(11);

		var result = await world.Converter.ConvertDatabaseAsync(await world.Parser.ParseFileAsync(FixturePath));

		await Assert.That(result.Errors.Any(e => e.Contains("#11") && e.Contains("Oracle Hall"))).IsTrue();
		await Assert.That((await NodeAsync(world, 11)).Object().Name).IsEqualTo("Squatter");
		await Assert.That((await NodeAsync(world, 9)).Object().Name).IsEqualTo("Gadget");
		await Assert.That((await NodeAsync(world, 12)).Object().Name).IsEqualTo("Attic");
	}

	/// <summary>
	/// PennMUSH has no ancestors, package manager or HTTP/event handlers, so the import removes the
	/// seeded ones that sat at #3-#9 and unsets the options that named them, rather than leaving the
	/// options pointing at imported players and things.
	/// </summary>
	[Test]
	public async Task TheSeededSystemObjectsAndTheirOptionsAreGone()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(await world.Parser.ParseFileAsync(FixturePath));

		var names = await world.Database.GetAllObjectsAsync().Select(o => o.Name).ToListAsync();
		foreach (var seeded in (string[])["Ancestor Room", "Ancestor Player", "Ancestor Exit", "Ancestor Thing",
							 "Package Manager", "HTTP Handler", "Event Handler"])
		{
			await Assert.That(names).DoesNotContain(seeded);
			await Assert.That(result.Warnings.Any(w => w.Contains(seeded))).IsTrue();
		}

		var stored = (await world.ExpandedData.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions)))!;
		await Assert.That(stored.Database.AncestorRoom).IsNull();
		await Assert.That(stored.Database.AncestorExit).IsNull();
		await Assert.That(stored.Database.AncestorThing).IsNull();
		await Assert.That(stored.Database.AncestorPlayer).IsNull();
		await Assert.That(stored.Database.PackageManager).IsNull();
		await Assert.That(stored.Database.HttpHandler).IsNull();
		await Assert.That(stored.Database.EventHandler).IsNull();
		await Assert.That(stored.Compatibility.ParenGroups).IsTrue();
	}

	/// <summary>
	/// A fresh server's bundled packages create their objects owned by, inside and homed at the Package
	/// Manager, among them the Scene Logger that SAY/POSE/@EMIT are hooked to. Removing the seeds must not
	/// leave such an object ownerless: every command it runs would throw "No owner found" and the hooked
	/// speech commands would say nothing. They go to God, as PennMUSH's dbck gives an ownerless object,
	/// and keep their flags.
	/// </summary>
	[Test]
	public async Task WhatTheSeedsOwnedOrHeldGoesToGod()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var packageManager = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(7)))).Expect<SharpPlayer>();
		var logger = await world.Database.CreateThingAsync("Scene Logger", packageManager, packageManager, packageManager);
		var loggerNode = await NodeAsync(world, logger.Number);
		var wizard = (await world.Database.GetObjectFlagAsync("WIZARD"))!;
		await world.Database.SetObjectFlagAsync(loggerNode, wizard);
		await world.Database.SetAttributeAsync(logger, ["CMD`SAY"], MarkupText.Plain("$say *:@message/spoof"), packageManager);

		var result = await world.Converter.ConvertDatabaseAsync(Dump(
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room }));

		await Assert.That(result.Errors).IsEmpty();
		var imported = await NodeAsync(world, logger.Number);
		await Assert.That(imported.Object().Name).IsEqualTo("Scene Logger");
		await Assert.That((await imported.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number)
			.IsEqualTo(1);
		await Assert.That((await imported.AsContent.Location()).Object().DBRef.Number).IsEqualTo(1);
		await Assert.That((await imported.AsContent.Home()).Object()!.DBRef.Number).IsEqualTo(1);
		await Assert.That(await SharpMUSH.Library.HelperFunctions.HasFlag(imported, "WIZARD")).IsTrue();
		var attribute = (await world.Database.GetAttributeAsync(logger, ["CMD`SAY"]).ToListAsync()).Single();
		await Assert.That((await attribute.Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.Number)
			.IsEqualTo(1);
		await Assert.That(result.Warnings.Any(w => w.StartsWith("Gave God (#1)") && w.Contains($"#{logger.Number}")))
			.IsTrue();
	}

	/// <summary>
	/// A seed is known by the creation time migration stamped on all of #0-#9, not by its name: a second
	/// import into a world whose #7 is an imported player named <c>Package Manager</c> deletes nothing.
	/// </summary>
	[Test]
	public async Task ASecondImportLeavesAnImportedObjectWithASeedNameAlone()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var first = await world.Converter.ConvertDatabaseAsync(Dump(
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 7, Name = "Package Manager", Type = PennMUSHObjectType.Player }));
		await Assert.That(first.Errors).IsEmpty();
		var imported = await NodeAsync(world, 7);

		var second = await world.Converter.ConvertDatabaseAsync(Dump(
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
			new PennMUSHObject { DBRef = 20, Name = "Later", Type = PennMUSHObjectType.Thing }));

		await Assert.That(second.Warnings.Any(w => w.StartsWith("Removed SharpMUSH's seeded"))).IsFalse();
		var survivor = await NodeAsync(world, 7);
		await Assert.That(survivor.Object().Name).IsEqualTo("Package Manager");
		await Assert.That(survivor.Object().CreationTime).IsEqualTo(imported.Object().CreationTime);
	}

	/// <summary>
	/// A source #2 that is not a room cannot become the Master Room, so it is imported under a new
	/// number — past every source object's, never onto a number a later source object keeps.
	/// </summary>
	[Test]
	public async Task ARelocatedLowObjectGoesPastTheHighestSourceDbref()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(Dump(
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
			new PennMUSHObject { DBRef = 2, Name = "Box", Type = PennMUSHObjectType.Thing },
			new PennMUSHObject { DBRef = 10, Name = "Ten", Type = PennMUSHObjectType.Thing }));

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That((await NodeAsync(world, 10)).Object().Name).IsEqualTo("Ten");
		await Assert.That((await NodeAsync(world, 11)).Object().Name).IsEqualTo("Box");
		await Assert.That((await NodeAsync(world, 2)).IsRoom).IsTrue();
		await Assert.That(result.Warnings.Any(w => w.Contains("#2 (Box)") && w.Contains("#11"))).IsTrue();
	}

	/// <summary>
	/// A lock naming a relocated object follows it to its new number, so <c>=#2</c> still gates on the
	/// thing that was #2 and not on the Master Room; a number in an attribute lock's value, even after a space, is a
	/// literal and is kept. Attribute text is softcode and stays as written,
	/// but the import names each attribute that mentions the old number.
	/// </summary>
	[Test]
	public async Task LocksFollowARelocatedObjectAndAttributesNamingItAreReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(Dump(
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
			new PennMUSHObject
			{
				DBRef = 2, Name = "Box", Type = PennMUSHObjectType.Thing,
				Attributes = [new PennMUSHAttribute { Name = "SELF", Value = "@pemit #2=hi" }]
			},
			new PennMUSHObject
			{
				DBRef = 10, Name = "Ten", Type = PennMUSHObjectType.Thing,
				Locks = new()
				{
					["Basic"] = "=#2|#1", ["Use"] = "@#2/Basic", ["Enter"] = "COUNT:#2",
					["Page"] = "TAG:foo #2|!(#2 & $#2)"
				},
				Attributes = [new PennMUSHAttribute { Name = "FOO", Value = "[name(#2)] #20 ##2" }]
			}));

		await Assert.That(result.Errors).IsEmpty();
		var ten = await NodeAsync(world, 10);
		await Assert.That(ten.Object().Locks["Basic"].LockString).IsEqualTo("=#11|#1");
		await Assert.That(ten.Object().Locks["Use"].LockString).IsEqualTo("@#11/Basic");
		await Assert.That(ten.Object().Locks["Enter"].LockString).IsEqualTo("COUNT:#2");
		await Assert.That(ten.Object().Locks["Page"].LockString).IsEqualTo("TAG:foo #2|!(#11 & $#11)");
		await Assert.That(result.Warnings.Any(w => w.Contains("#10 Basic lock") && w.Contains("'=#2|#1' became '=#11|#1'"))).IsTrue();
		await Assert.That(result.Warnings.Any(w => w.Contains("#10 Enter lock"))).IsFalse();

		await Assert.That(await AttributeAsync(world, 10, "FOO")).IsEqualTo("[name(#2)] #20 ##2");
		await Assert.That(result.Warnings.Any(w => w.StartsWith("2 attribute(s) mention #2, which was imported as #11")
			&& w.EndsWith(": #11/SELF, #10/FOO"))).IsTrue();
	}

	/// <summary>
	/// A minimal PennMUSH world is #0-#2. With the seeds at #3-#9 gone, the next object is #3, one past
	/// the highest imported object, not the 10 the seeds had pushed the counter to.
	/// </summary>
	[Test]
	public async Task AMinimalDumpLeavesTheCounterOnePastItsHighestObject()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(Dump(
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room }));
		await Assert.That(result.Errors).IsEmpty();
		var god = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var limbo = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<SharpRoom>();

		var created = await world.Database.CreateThingAsync("After Import", limbo, god, limbo);

		await Assert.That(created.Number).IsEqualTo(3);
	}

	private static PennMUSHDatabase Dump(params PennMUSHObject[] objects)
		=> new() { Version = "Test Version", Objects = [.. objects] };

	private static async Task<IsolatedImportWorld> ImportAsync()
	{
		var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(await world.Parser.ParseFileAsync(FixturePath));
		await Assert.That(result.Errors).IsEmpty();
		return world;
	}

	internal static async Task<AnySharpObject> NodeAsync(IsolatedImportWorld world, int dbref)
		=> (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)))).Expect<AnySharpObject>();

	private static async Task<string> AttributeAsync(IsolatedImportWorld world, int dbref, string name)
		=> (await world.Database.GetAttributeAsync(new DBRef(dbref), [name]).ToListAsync()).Single().Value.ToPlainText();
}
