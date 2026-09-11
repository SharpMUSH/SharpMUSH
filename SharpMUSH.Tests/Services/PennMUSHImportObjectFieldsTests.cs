using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The fields of a PennMUSH object beyond where it is: who owns it, where its home or drop-to is, its
/// flags, powers and warnings, and a player's quota. Each is read back through the engine.
/// </summary>
public class PennMUSHImportObjectFieldsTests
{
	/// <summary>
	/// Alice owns herself, the hall and the widget; Bob owns himself and the door. The hall drops to
	/// Room Zero, Alice and the widget are homed in the hall, Bob in Room Zero.
	/// </summary>
	private static PennMUSHDatabase Fixture() => new()
	{
		Version = "Fields Fixture",
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject
			{
				DBRef = 3, Name = "Alice", Type = PennMUSHObjectType.Player, Owner = 3, Location = 10, Link = 10,
				Flags = ["ENTER_OK", "ANSI", "CONNECTED"], Warnings = ["normal"], Pennies = 175,
				Attributes = [new PennMUSHAttribute { Name = "RQUOTA", Value = "20", Flags = ["mortal_dark", "locked"] }]
			},
			new PennMUSHObject
			{
				DBRef = 4, Name = "Bob", Type = PennMUSHObjectType.Player, Owner = 4, Location = 10, Link = 0,
				Flags = ["UNINSPECTED"], Powers = ["See_All", "QUOTAS"]
			},
			new PennMUSHObject { DBRef = 10, Name = "Fields Hall", Type = PennMUSHObjectType.Room, Owner = 3, Link = 0 },
			new PennMUSHObject
			{
				DBRef = 11, Name = "Fields Widget", Type = PennMUSHObjectType.Thing, Owner = 3, Location = 10, Link = 10,
				Flags = ["DARK", "NO_COMMAND"], Warnings = ["exit-unlinked"], Pennies = 10
			},
			new PennMUSHObject { DBRef = 12, Name = "Fields Door", Type = PennMUSHObjectType.Exit, Owner = 4, Location = 10, Link = 0 }
		]
	};

	/// <summary>PennMUSH's owner field, resolved through the conversion's mapping; a player owns itself.</summary>
	[Test]
	public async Task EveryObjectBelongsToItsSourceOwner()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await ImportAsync(world);

		var alice = await FindAsync(world, "Alice");
		var bob = await FindAsync(world, "Bob");
		foreach (var (name, owner) in ((string, AnySharpObject)[])
			[("Alice", alice), ("Fields Hall", alice), ("Fields Widget", alice), ("Bob", bob), ("Fields Door", bob)])
		{
			var obj = await FindAsync(world, name);
			var actual = await obj.Object().Owner.WithCancellation(CancellationToken.None);
			await Assert.That(actual.Object.Key).IsEqualTo(owner.Object().Key)
				.Because($"{name} should belong to {owner.Object().Name}");
		}
	}

	/// <summary>PennMUSH keeps a thing's or player's home, and a room's drop-to, in its link.</summary>
	[Test]
	public async Task HomesAndDropTosComeFromTheSourceLink()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await ImportAsync(world);

		var hall = await FindAsync(world, "Fields Hall");

		var widgetHome = await (await FindAsync(world, "Fields Widget")).Expect<SharpThing>().Home.WithCancellation(CancellationToken.None);
		await Assert.That(widgetHome.Object().Key).IsEqualTo(hall.Object().Key);

		var aliceHome = await (await FindAsync(world, "Alice")).Expect<SharpPlayer>().Home.WithCancellation(CancellationToken.None);
		await Assert.That(aliceHome.Object().Key).IsEqualTo(hall.Object().Key);

		var bobHome = await (await FindAsync(world, "Bob")).Expect<SharpPlayer>().Home.WithCancellation(CancellationToken.None);
		await Assert.That(bobHome.Object().Key).IsEqualTo(0);

		var dropTo = await hall.Expect<SharpRoom>().Location.WithCancellation(CancellationToken.None);
		await Assert.That(dropTo.Object()!.Key).IsEqualTo(0);
	}

	/// <summary>
	/// Imported objects are created without this game's default flags, so the source's are the whole
	/// truth. CONNECTED is dropped as PennMUSH itself drops it on every load; a flag or power SharpMUSH
	/// has no counterpart for is reported, not silently lost.
	/// </summary>
	[Test]
	public async Task FlagsAndPowersAreTheSourcesAndUnknownOnesAreReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world);

		await Assert.That(await FlagNamesAsync(world, "Fields Widget")).IsEquivalentTo(["DARK", "NO_COMMAND"]);
		await Assert.That(await FlagNamesAsync(world, "Alice")).IsEquivalentTo(["ENTER_OK", "ANSI"]);

		var bob = await FindAsync(world, "Bob");
		var powers = await bob.Object().Powers.Value.Select(p => p.Name).ToListAsync();
		await Assert.That(powers).IsEquivalentTo(["See_All"]);

		await Assert.That(result.Warnings.Any(w => w.Contains("UNINSPECTED"))).IsTrue();
		await Assert.That(result.Warnings.Any(w => w.Contains("QUOTAS"))).IsTrue();
		await Assert.That(result.Warnings.Any(w => w.Contains("CONNECTED"))).IsFalse();
	}

	[Test]
	public async Task WarningsAreTheSources()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await ImportAsync(world);

		await Assert.That((await FindAsync(world, "Fields Widget")).Object().Warnings).IsEqualTo(WarningType.ExitUnlinked);
		await Assert.That((await FindAsync(world, "Alice")).Object().Warnings).IsEqualTo(WarningType.Normal);
	}

	/// <summary>
	/// SharpMUSH's quota is a player's limit; PennMUSH keeps what is left of it in RQUOTA (src/wiz.c),
	/// so the limit is what the player owns plus that. Pennies are money, which SharpMUSH does not
	/// track: they are reported, never taken for quota.
	/// </summary>
	[Test]
	public async Task QuotaIsTheSourcesLimitAndPenniesAreReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world);

		var alice = (await FindAsync(world, "Alice")).Expect<SharpPlayer>();
		await Assert.That(alice.Quota).IsEqualTo(2 + 20)
			.Because("Alice owns the hall and the widget and has 20 left");
		await Assert.That(await world.Database.GetAttributeAsync(alice.Object.DBRef, ["RQUOTA"]).ToListAsync()).IsEmpty();
		await Assert.That(result.Warnings.Any(w => w.Contains("pennies", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}

	private static async Task<ConversionResult> ImportAsync(IsolatedImportWorld world)
	{
		var result = await world.Converter.ConvertDatabaseAsync(Fixture());
		await Assert.That(result.Errors).IsEmpty();
		return result;
	}

	/// <summary>An object's flags, less the one SharpMUSH lists for its type.</summary>
	private static async Task<List<string>> FlagNamesAsync(IsolatedImportWorld world, string name)
	{
		var obj = (await FindAsync(world, name)).Object();
		return await obj.Flags.Value.Select(f => f.Name).Where(f => f != obj.Type).ToListAsync();
	}

	private static async Task<AnySharpObject> FindAsync(IsolatedImportWorld world, string name)
	{
		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == name)).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}
}
