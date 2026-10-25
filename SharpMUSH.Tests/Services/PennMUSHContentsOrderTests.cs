using SharpMUSH.Library.Services.DatabaseConversion;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The converter places objects so that each container lists its contents in the order of PennMUSH's
/// contents list (<c>Contents</c>, then each object's <c>Next</c>), newest arrival first.
/// </summary>
public class PennMUSHContentsOrderTests
{
	private static PennMUSHObject Room(int dbref, int contents)
		=> new() { DBRef = dbref, Name = $"Room{dbref}", Type = PennMUSHObjectType.Room, Contents = contents };

	private static PennMUSHObject Thing(int dbref, int location, int next)
		=> new() { DBRef = dbref, Name = $"Thing{dbref}", Type = PennMUSHObjectType.Thing, Location = location, Next = next };

	[Test]
	public async Task TheHeadOfEachListIsPlacedLastAndStraysFirst()
	{
		// Room 0 lists 3, 1, 4; room 2 lists 5. Thing 6 claims room 0 but no list reaches it.
		var database = new PennMUSHDatabase
		{
			Version = "Test",
			Objects =
			[
				Room(0, 3), Thing(1, 0, 4), Room(2, 5), Thing(3, 0, 1), Thing(4, 0, -1), Thing(5, 2, -1), Thing(6, 0, -1)
			]
		};

		var order = PennMUSHDatabaseConverter.ArrivalOrder(database).Select(o => o.DBRef).ToList();

		await Assert.That(order.Where(d => d is 1 or 3 or 4 or 6)).IsEquivalentTo([6, 4, 1, 3], CollectionOrdering.Matching);
		await Assert.That(order.IndexOf(5)).IsGreaterThan(order.IndexOf(2));
		await Assert.That(order).Count().IsEqualTo(7);
	}

	[Test]
	public async Task ACycleInAListEndsIt()
	{
		var database = new PennMUSHDatabase { Version = "Test", Objects = [Room(0, 1), Thing(1, 0, 2), Thing(2, 0, 1)] };

		var order = PennMUSHDatabaseConverter.ArrivalOrder(database).Select(o => o.DBRef).ToList();

		await Assert.That(order).IsEquivalentTo([0, 2, 1], CollectionOrdering.Matching);
	}
}
