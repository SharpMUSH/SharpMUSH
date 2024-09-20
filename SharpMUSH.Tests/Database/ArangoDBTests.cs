using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class ArangoDBTests : BaseUnitTest
{
	private static ISharpDatabase? database;

	[Before(Test)]
	public async Task OneTimeSetup()
	{
		database = await IntegrationServer();
	}

	[Test]
	public async Task TestRoomZero()
	{
		var roomZero = (await database!.GetObjectNodeAsync(new DBRef(0))).AsRoom;

		await Assert.That(roomZero.GetType()).IsTypeOf(typeof(SharpRoom));
		await Assert.That(roomZero!.Object!.Name).IsEqualTo("Room Zero");
		await Assert.That(roomZero!.Object!.Key).IsEqualTo(0);
	}

	[Test]
	public async Task TestRoomTwo()
	{
		var masterRoom = (await database!.GetObjectNodeAsync(new DBRef(2))).AsRoom;

		await Assert.That(masterRoom.GetType()).IsTypeOf(typeof(SharpRoom));
		await Assert.That(masterRoom!.Object!.Name).IsEqualTo("Master Room");
		await Assert.That(masterRoom!.Object!.Key).IsEqualTo(2);
	}

	[Test]
	public async Task TestPlayerOne()
	{
		var playerOne = (await database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne.GetType()).IsEqualTo(typeof(SharpPlayer));
		await Assert.That(playerOne!.Object!.Name).IsEqualTo("God");
		await Assert.That(playerOne!.Object!.Key).IsEqualTo(1);
	}

	[Test]
	public async Task SetAndGetAnAttribute()
	{
		var playerOne = (await database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne.GetType()).IsEqualTo(typeof(SharpPlayer));
		await Assert.That(playerOne.Object.Name).IsEqualTo("God");
		await Assert.That(playerOne.Object.Key).IsEqualTo(1);

		var playerOneDBRef = new DBRef(playerOne!.Object.Key);

		await database.SetAttributeAsync(playerOneDBRef, ["SingleLayer"], "Single", playerOne);
		await database.SetAttributeAsync(playerOneDBRef, ["Two"], "Twin", playerOne);
		await database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], "Layer", playerOne);
		await database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves"], "Leaf", playerOne);
		await database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"], "Leaf2", playerOne);
		await database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"], "Deep1", playerOne);
		await database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"], "Deeper", playerOne);

		var existingSingle = (await database.GetAttributeAsync(playerOneDBRef, ["SingleLayer"]))!.ToList();
		var existingLayer = (await database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToList();
		var existingLeaf = (await database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves"]))!.ToList();
		var existingLeaf2 = (await database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"]))!.ToList();
		var existingDeep1 = (await database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"]))!.ToList();
		var existingDeep2 = (await database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"]))!.ToList();

		var obj = await database!.GetObjectNodeAsync(playerOneDBRef);

		await Assert.That(existingSingle!.Count()).IsEqualTo(1);
		await Assert.That(existingLayer!.Count()).IsEqualTo(2);
		await Assert.That(existingLeaf!.Count()).IsEqualTo(2);
		await Assert.That(existingLeaf2!.Count()).IsEqualTo(2);
		await Assert.That(existingDeep1!.Count()).IsEqualTo(32);
		await Assert.That(existingDeep2!.Count()).IsEqualTo(3);
		await Assert.That(existingSingle!.Last().Value).IsEqualTo("Single");
		await Assert.That(existingLayer!.Last().Value).IsEqualTo("Layer");
		await Assert.That(existingLeaf!.Last().Value).IsEqualTo("Leaf");
		await Assert.That(existingLeaf2!.Last().Value).IsEqualTo("Leaf2");
		await Assert.That(existingDeep1!.Last().Value).IsEqualTo("Deep1");
		await Assert.That(existingDeep2!.Last().Value).IsEqualTo("Deeper");
		await Assert.That(existingSingle!.Last().LongName).IsEqualTo("SINGLELAYER");
		await Assert.That(existingLayer!.Last().LongName).IsEqualTo("TWO`LAYERS");
		await Assert.That(existingLeaf!.Last().LongName).IsEqualTo("TWO`LEAVES");
		await Assert.That(existingLeaf2!.Last().LongName).IsEqualTo("TWO`LEAVES2");
		await Assert.That(existingDeep1!.Last().LongName).IsEqualTo("THREE`LAYERS`DEEP");
		await Assert.That(existingDeep2!.Last().LongName).IsEqualTo("THREE`LAYERS`DEEP2");
		await Assert.That(existingDeep1!.Skip(1).First().LongName).IsEqualTo("THREE`LAYERS");
		await Assert.That(existingDeep2!.Skip(1).First().LongName).IsEqualTo("THREE`LAYERS");

		var attributes = obj.Object()!.Attributes();

		foreach (var attribute in attributes)
		{
			await Assert.That(attribute)
				.IsTypeOf(typeof(SharpAttribute))
				.And
				.IsNotNull();
		}
	}
}