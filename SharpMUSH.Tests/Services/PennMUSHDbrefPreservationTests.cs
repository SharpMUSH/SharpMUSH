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
			await Assert.That(await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(hole)))).IsTypeOf<None>();
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
		await Assert.That((await hall.Expect<SharpRoom>().Location.WithCancellation(CancellationToken.None)).Object()!.Key).IsEqualTo(12);

		var north = (await NodeAsync(world, 14)).Expect<SharpExit>();
		await Assert.That((await north.Location.WithCancellation(CancellationToken.None)).Object().Key).IsEqualTo(12);
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
		var squatter = await world.Database.CreateThingAsync("Squatter", limbo, god, limbo, requestedDbref: 6);
		await Assert.That(squatter.Number).IsEqualTo(6);

		var result = await world.Converter.ConvertDatabaseAsync(await world.Parser.ParseFileAsync(FixturePath));

		await Assert.That(result.Errors.Any(e => e.Contains("#6") && e.Contains("Widget"))).IsTrue();
		await Assert.That((await NodeAsync(world, 6)).Object().Name).IsEqualTo("Squatter");
		await Assert.That((await NodeAsync(world, 9)).Object().Name).IsEqualTo("Gadget");
	}

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
