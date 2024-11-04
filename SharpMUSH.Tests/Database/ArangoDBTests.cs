using SharpMUSH.IntegrationTests;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class ArangoDBTests : BaseUnitTest
{
		private static Infrastructure? _server;
		private static ISharpDatabase? _database;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		(_database,_server) = await IntegrationServer();
	}

	[After(Class)]
	public static async Task OneTimeTearDown()
	{
		_server!.Dispose();
		await Task.CompletedTask;
	}

	[Test]
	public async Task TestRoomZero()
	{
		var roomZero = (await _database!.GetObjectNodeAsync(new DBRef(0))).AsRoom;

		await Assert.That(roomZero).IsTypeOf(typeof(SharpRoom));
		await Assert.That(roomZero!.Object!.Name).IsEqualTo("Room Zero");
		await Assert.That(roomZero!.Object!.Key).IsEqualTo(0);
	}

	[Test]
	public async Task TestRoomTwo()
	{
		var masterRoom = (await _database!.GetObjectNodeAsync(new DBRef(2))).AsRoom;

		await Assert.That(masterRoom).IsTypeOf(typeof(SharpRoom));
		await Assert.That(masterRoom!.Object!.Name).IsEqualTo("Master Room");
		await Assert.That(masterRoom!.Object!.Key).IsEqualTo(2);
	}

	[Test]
	public async Task TestPlayerOne()
	{
		var playerOne = (await _database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne).IsTypeOf<SharpPlayer>();
		await Assert.That(playerOne!.Object!.Name).IsEqualTo("God");
		await Assert.That(playerOne!.Object!.Key).IsEqualTo(1);
	}

	[Test, Skip("Too fragile.")]
	public async Task SetAndOverrideAnAttribute()
	{
		
		var playerOne = (await _database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = new DBRef(playerOne!.Object.Key);

		await _database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], "Layer", playerOne);
		var existingLayer = (await _database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToList();

		await Assert.That(existingLayer.Last().Value.ToString()).IsEqualTo("Layer");

		await Task.Delay(500); // TODO: This is there to avoid write-write conflicts.
		await _database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], "Layer2", playerOne);
		var overwrittenLayer = (await _database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToList();

		await Assert.That(overwrittenLayer.Last().Value.ToString()).IsEqualTo("Layer2");
	}

	[Test, Skip("Too fragile.")]
	// [Repeat(10)] // Exclusive Locks are needed first. Otherwise there will be write-write errors. 
	public async Task SetAndGetAnAttribute()
	{
		var playerOne = (await _database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne).IsTypeOf<SharpPlayer>();
		await Assert.That(playerOne.Object.Name).IsEqualTo("God");
		await Assert.That(playerOne.Object.Key).IsEqualTo(1);

		var playerOneDBRef = new DBRef(playerOne!.Object.Key);

		await _database.SetAttributeAsync(playerOneDBRef, ["SingleLayer"], "Single", playerOne);
		await _database.SetAttributeAsync(playerOneDBRef, ["Two"], "Twin", playerOne);
		await _database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], "Layer", playerOne);
		await _database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves"], "Leaf", playerOne);
		await _database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"], "Leaf2", playerOne);
		await _database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"], "Deep1", playerOne);
		await _database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"], "Deeper", playerOne);

		var existingSingle = (await _database.GetAttributeAsync(playerOneDBRef, ["SingleLayer"]))?.ToList();
		var existingLayer = (await _database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))?.ToList();
		var existingLeaf = (await _database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves"]))?.ToList();
		var existingLeaf2 = (await _database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"]))?.ToList();
		var existingDeep1 = (await _database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"]))?.ToList();
		var existingDeep2 = (await _database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"]))?.ToList();

		var obj = await _database!.GetObjectNodeAsync(playerOneDBRef);

		await Assert.That(existingSingle!.Count).IsEqualTo(1);
		await Assert.That(existingLayer!.Count).IsEqualTo(2);
		await Assert.That(existingLeaf!.Count).IsEqualTo(2);
		await Assert.That(existingLeaf2!.Count).IsEqualTo(2);
		await Assert.That(existingDeep1!.Count).IsEqualTo(3);
		await Assert.That(existingDeep2!.Count).IsEqualTo(3);
		await Assert.That(existingSingle.Last().Value.ToString()).IsEqualTo("Single");
		await Assert.That(existingLayer.Last().Value.ToString()).IsEqualTo("Layer");
		await Assert.That(existingLeaf.Last().Value.ToString()).IsEqualTo("Leaf");
		await Assert.That(existingLeaf2.Last().Value.ToString()).IsEqualTo("Leaf2");
		await Assert.That(existingDeep1.Last().Value.ToString()).IsEqualTo("Deep1");
		await Assert.That(existingDeep2.Last().Value.ToString()).IsEqualTo("Deeper");
		await Assert.That(existingSingle.Last().LongName).IsEqualTo("SINGLELAYER");
		await Assert.That(existingLayer.Last().LongName).IsEqualTo("TWO`LAYERS");
		await Assert.That(existingLeaf.Last().LongName).IsEqualTo("TWO`LEAVES");
		await Assert.That(existingLeaf2.Last().LongName).IsEqualTo("TWO`LEAVES2");
		await Assert.That(existingDeep1.Last().LongName).IsEqualTo("THREE`LAYERS`DEEP");
		await Assert.That(existingDeep2.Last().LongName).IsEqualTo("THREE`LAYERS`DEEP2");
		await Assert.That(existingDeep1.Skip(1).First().LongName).IsEqualTo("THREE`LAYERS");
		await Assert.That(existingDeep2.Skip(1).First().LongName).IsEqualTo("THREE`LAYERS");

		var attributes = obj.Object()!.Attributes();

		foreach (var attribute in attributes)
		{
			await Assert.That(attribute)
				.IsTypeOf<SharpAttribute>()
				.And
				.IsNotNull();
		}
	}
}