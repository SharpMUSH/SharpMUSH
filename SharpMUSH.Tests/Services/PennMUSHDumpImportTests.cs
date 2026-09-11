using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A real PennMUSH dump, parsed and imported: the world <see cref="PennMUSHDatabaseParserTests"/>
/// describes, arriving whole in a SharpMUSH one.
/// </summary>
public class PennMUSHDumpImportTests
{
	private static readonly string FixturePath =
		Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0.outdb");

	/// <summary>
	/// The passwords PennMUSH 1.8.8 wrote for <c>@pcreate Alice=alicepass1</c> and
	/// <c>@pcreate Bob=bobpass22</c> still open those characters.
	/// </summary>
	[Test]
	[Arguments("Alice", "alicepass1")]
	[Arguments("Bob", "bobpass22")]
	public async Task ImportedPlayersLogInWithTheirPennMUSHPasswords(string name, string password)
	{
		await using var world = await ImportAsync();
		var player = (await FindAsync(world, name)).Expect<SharpPlayer>();
		var key = $"#{player.Object.Key}:{player.Object.CreationTime}";

		await Assert.That(world.Passwords.PasswordIsValid(key, password, player.PasswordHash)).IsTrue();
		await Assert.That(world.Passwords.PasswordIsValid(key, "wrongpass", player.PasswordHash)).IsFalse();
	}

	/// <summary>The hash lives in the password field only, never as an attribute softcode could read.</summary>
	[Test]
	public async Task NoImportedObjectCarriesXyxxy()
	{
		await using var world = await ImportAsync();

		foreach (var name in (string[])["Alice", "Bob"])
		{
			var player = await FindAsync(world, name);
			var xyxxy = await world.Database.GetAttributeAsync(player.Object().DBRef, ["XYXXY"]).ToListAsync();
			await Assert.That(xyxxy).IsEmpty()
				.Because($"{name}'s password hash was imported as an attribute");
		}
	}

	[Test]
	public async Task ExitsSitInTheirSourceLeadToTheirDestinationAndKeepTheirAliases()
	{
		await using var world = await ImportAsync();

		var north = (await FindAsync(world, "North")).Expect<SharpExit>();
		var hall = await FindAsync(world, "Oracle Hall");

		await Assert.That(north.Aliases).IsEquivalentTo(["n"]);
		await Assert.That((await north.Location.WithCancellation(CancellationToken.None)).Object().Key).IsEqualTo(0);
		await Assert.That((await north.Home.WithCancellation(CancellationToken.None)).Object()!.Key).IsEqualTo(hall.Object().Key);
	}

	[Test]
	public async Task ThingsArriveWhereTheyWereWithTheirParentZoneLocksAndAttributes()
	{
		await using var world = await ImportAsync();

		var widget = await FindAsync(world, "Widget");
		var gadget = await FindAsync(world, "Gadget");
		var hall = await FindAsync(world, "Oracle Hall");

		await Assert.That((await widget.Expect<SharpThing>().Location.WithCancellation(CancellationToken.None)).Object().Key)
			.IsEqualTo(hall.Object().Key);
		await Assert.That((await widget.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key)
			.IsEqualTo(gadget.Object().Key);
		await Assert.That((await widget.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key)
			.IsEqualTo(gadget.Object().Key);
		await Assert.That(widget.Object().Locks.Keys).Contains("Use");

		var note = await world.Database.GetAttributeAsync(widget.Object().DBRef, ["NOTE"]).ToListAsync();
		await Assert.That(note.Single().Value.ToPlainText()).IsEqualTo("He said \"hi\" \\ back");
	}

	private static async Task<IsolatedImportWorld> ImportAsync()
	{
		var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(await world.Parser.ParseFileAsync(FixturePath));
		await Assert.That(result.Errors).IsEmpty();
		return world;
	}

	private static async Task<AnySharpObject> FindAsync(IsolatedImportWorld world, string name)
	{
		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == name)).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}
}
