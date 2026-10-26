using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// References to objects the source no longer has. PennMUSH's dbck repairs them when the game loads, so an
/// old game can carry a few; the import repairs them the same way and says what it did with each.
/// </summary>
public class PennMUSHDanglingReferenceTests
{
	/// <summary>
	/// Lost is in #99, parented to #98 and zoned to #97, with its home in Dangling Home. Stray is in #95 and
	/// has no home. The exit leads to #96. Orphan's owner is #94. Misled is in #93 with the exit for its home. Selfhome is in #92 and is its own home.
	/// </summary>
	private static PennMUSHDatabase Fixture() => new()
	{
		Version = "Dangling Fixture",
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject
			{
				DBRef = 3, Name = "Dangling Lost", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 99, Link = 4,
				Parent = 98, Zone = 97
			},
			new PennMUSHObject { DBRef = 4, Name = "Dangling Home", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 5, Name = "Dangling Exit", Type = PennMUSHObjectType.Exit, Owner = 1, Location = 4, Link = 96 },
			new PennMUSHObject { DBRef = 6, Name = "Dangling Stray", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 95 },
			new PennMUSHObject { DBRef = 7, Name = "Dangling Orphan", Type = PennMUSHObjectType.Thing, Owner = 94, Location = 4, Link = 4 },
			new PennMUSHObject { DBRef = 8, Name = "Dangling Misled", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 93, Link = 5 },
			new PennMUSHObject { DBRef = 9, Name = "Dangling Selfhome", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 92, Link = 9 }
		]
	};

	[Test]
	public async Task EachDanglingReferenceIsRepairedAndReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(Fixture());

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.Warnings.Texts()).Contains("Location #99 of #3 is not in the import; it was put in its home #4");
		await Assert.That(result.Warnings.Texts()).Contains("Parent #98 of #3 is not in the import; it was dropped");
		await Assert.That(result.Warnings.Texts()).Contains("Zone #97 of #3 is not in the import; it was dropped");
		await Assert.That(result.Warnings.Texts()).Contains("Destination #96 of exit #5 is not in the import; the exit was left unlinked");
		await Assert.That(result.Warnings.Texts()).Contains("Location #95 of #6 is not in the import; it was left in #0");
		await Assert.That(result.Warnings.Texts()).Contains("Owner #94 of #7 is not an imported player; God keeps it");
		await Assert.That(result.Warnings.Texts()).Contains("Location #93 of #8 is not in the import, and its home #5 cannot hold it; it was left in #0");
		await Assert.That(result.Warnings.Texts()).DoesNotContain("Location #93 of #8 is not in the import; it was put in its home #5");
		await Assert.That(result.Warnings.Texts()).Contains("Location #92 of #9 is not in the import; it was left in #0");

		var home = await FindAsync(world, "Dangling Home");
		var lost = await FindAsync(world, "Dangling Lost");
		var lostLocation = await lost.Expect<SharpThing>().Location.WithCancellation(CancellationToken.None);
		await Assert.That(lostLocation.Object().Key).IsEqualTo(home.Object().Key);
		await Assert.That(await lost.Object().Parent.WithCancellation(CancellationToken.None) is None).IsTrue();
		await Assert.That(await lost.Object().Zone.WithCancellation(CancellationToken.None) is None).IsTrue();

		var stray = await FindAsync(world, "Dangling Stray");
		var strayLocation = await stray.Expect<SharpThing>().Location.WithCancellation(CancellationToken.None);
		await Assert.That(strayLocation.Object().Key).IsEqualTo(0);

		var selfhome = await FindAsync(world, "Dangling Selfhome");
		var selfhomeLocation = await selfhome.Expect<SharpThing>().Location.WithCancellation(CancellationToken.None);
		await Assert.That(selfhomeLocation.Object().Key).IsEqualTo(0);
	}

	/// <summary>The portal's preflight no longer refuses a file for one: the conversion repairs it.</summary>
	[Test]
	public async Task PreflightAcceptsADanglingReference()
	{
		var source = Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0-holes.outdb");
		var copy = Path.Join(Path.GetTempPath(), $"dangling-{Guid.NewGuid():N}.outdb");
		// The Widget, #6, is parented to the Gadget, #9; #7 is one of the dump's holes.
		var text = await File.ReadAllTextAsync(source);
		var parent = text.IndexOf("parent #9", text.IndexOf("name \"Widget\"", StringComparison.Ordinal), StringComparison.Ordinal);
		await File.WriteAllTextAsync(copy, text[..parent] + "parent #7" + text[(parent + "parent #9".Length)..]);
		try
		{
			var database = await new DatabaseImportPreflight(new PennMUSHDatabaseParser(NullLogger<PennMUSHDatabaseParser>.Instance))
				.ReadAsync(new DatabaseImportFiles(copy));

			await Assert.That(database.GetObject(6)!.Parent).IsEqualTo(7);
		}
		finally
		{
			File.Delete(copy);
		}
	}

	private static async Task<AnySharpObject> FindAsync(IsolatedImportWorld world, string name)
	{
		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == name)).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}
}
