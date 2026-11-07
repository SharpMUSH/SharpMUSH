using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// An exit leading HOME, a variable exit and a room whose drop-to is HOME: the source stores the pseudo-dbrefs
/// -3 and -2 in the link field, and the import records each as <c>_LINKTYPE</c>, as <c>@link</c> does. The
/// TinyMUX and RhostMUSH readers hand the converter the same fields, so this covers every dialect.
/// </summary>
public class PennMUSHImportLinkTypeTests
{
	private static PennMUSHDatabase Fixture() => new()
	{
		Version = "Link Type Fixture",
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 3, Name = "Home Drop Room", Type = PennMUSHObjectType.Room, Owner = 1, Link = -3 },
			new PennMUSHObject { DBRef = 4, Name = "Home Exit", Type = PennMUSHObjectType.Exit, Owner = 1, Location = 0, Link = -3 },
			new PennMUSHObject { DBRef = 5, Name = "Variable Exit", Type = PennMUSHObjectType.Exit, Owner = 1, Location = 0, Link = -2 },
			new PennMUSHObject { DBRef = 6, Name = "Plain Exit", Type = PennMUSHObjectType.Exit, Owner = 1, Location = 0, Link = 3 },
			new PennMUSHObject { DBRef = 7, Name = "Plain Room", Type = PennMUSHObjectType.Room, Owner = 1, Link = 3 }
		]
	};

	[Test]
	public async Task HomeAndVariableLinksAreRecorded()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Fixture());
		await Assert.That(result.Errors).IsEmpty();

		await Assert.That(await LinkTypeAsync(world, "Home Exit")).IsEqualTo("home");
		await Assert.That(await LinkTypeAsync(world, "Variable Exit")).IsEqualTo("variable");
		await Assert.That(await LinkTypeAsync(world, "Home Drop Room")).IsEqualTo("home");
		await Assert.That(await LinkTypeAsync(world, "Plain Exit")).IsNull();
		await Assert.That(await LinkTypeAsync(world, "Plain Room")).IsNull();

		var plain = (await ObjectAsync(world, "Plain Exit")).Expect<SharpExit>();
		await Assert.That((await plain.Home.WithCancellation(CancellationToken.None)).Object()!.Name).IsEqualTo("Home Drop Room");
	}

	[Test]
	public async Task RecordedLinkTypeBelongsToTheObjectsOwner()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await world.Converter.ConvertDatabaseAsync(Fixture());

		var exit = await ObjectAsync(world, "Home Exit");
		var attribute = (await world.Mediator.CreateStream(new GetAttributeQuery(exit.Object().DBRef, ["_LINKTYPE"])).ToArrayAsync()).Single();
		var owner = await attribute.Owner.WithCancellation(CancellationToken.None);
		var exitOwner = await exit.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(owner!.Object.DBRef.Number).IsEqualTo(exitOwner.Object.DBRef.Number);
	}

	private static async Task<AnySharpObject> ObjectAsync(IsolatedImportWorld world, string name)
	{
		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == name)).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}

	private static async Task<string?> LinkTypeAsync(IsolatedImportWorld world, string name)
	{
		var obj = await ObjectAsync(world, name);
		var attributes = await world.Mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, ["_LINKTYPE"])).ToArrayAsync();
		return attributes.Length == 0 ? null : attributes[^1].Value.ToPlainText();
	}
}
