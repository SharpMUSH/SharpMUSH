using SharpMUSH.Tests;
using SharpMUSH.Library.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.IntegrationTests;

[TestClass]
public class ArangoDBTests : BaseUnitTest
{
	private static ISharpDatabase? database;

	[ClassInitialize()]
	public static async Task OneTimeSetup(TestContext _)
	{
		database = await IntegrationServer();
	}

	[TestMethod]
	public async Task TestRoomZero()
	{
		var roomZero = (await database!.GetObjectNodeAsync(new DBRef(0))).AsRoom;

		Assert.AreEqual(typeof(SharpRoom), roomZero.GetType());
		Assert.AreEqual("Room Zero", roomZero!.Object!.Name);
		Assert.AreEqual(0, roomZero!.Object!.Key);
	}

	[TestMethod]
	public async Task TestRoomTwo()
	{
		var masterRoom = (await database!.GetObjectNodeAsync(new DBRef(2))).AsRoom;

		Assert.AreEqual(typeof(SharpRoom), masterRoom.GetType());
		Assert.AreEqual("Master Room", masterRoom!.Object!.Name);
		Assert.AreEqual(2, masterRoom!.Object!.Key);
	}

	[TestMethod]
	public async Task TestPlayerOne()
	{
		var playerOne = (await database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		Assert.AreEqual(typeof(SharpPlayer), playerOne.GetType());
		Assert.AreEqual("God", playerOne!.Object!.Name);
		Assert.AreEqual(1, playerOne!.Object!.Key);
	}

	[TestMethod]
	public async Task SetAndGetAnAttribute()
	{
		var playerOne = (await database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		Assert.AreEqual(typeof(SharpPlayer), playerOne.GetType());
		Assert.AreEqual("God", playerOne.Object.Name);
		Assert.AreEqual(1, playerOne.Object.Key);

		var playerOneDBRef = new DBRef(playerOne!.Object.Key);

		await database!.SetAttributeAsync(playerOneDBRef, ["SingleLayer"], "Single", playerOne);
		await database!.SetAttributeAsync(playerOneDBRef, ["Two"], "Twin", playerOne);
		await database!.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], "Layer", playerOne);
		await database!.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves"], "Leaf", playerOne);
		await database!.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"], "Leaf2", playerOne);
		await database!.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"], "Deep1", playerOne);
		await database!.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"], "Deeper", playerOne);

		var existingSingle = await database!.GetAttributeAsync(playerOneDBRef, ["SingleLayer"]);
		var existingLayer = await database!.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]);
		var existingLeaf = await database!.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves"]);
		var existingLeaf2 = await database!.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"]);
		var existingDeep1 = await database!.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"]);
		var existingDeep2 = await database!.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"]);

		var obj = await database!.GetObjectNodeAsync(playerOneDBRef);

		Assert.AreEqual(1, existingSingle!.Count());
		Assert.AreEqual(2, existingLayer!.Count());
		Assert.AreEqual(2, existingLeaf!.Count());
		Assert.AreEqual(2, existingLeaf2!.Count());
		Assert.AreEqual(3, existingDeep1!.Count());
		Assert.AreEqual(3, existingDeep2!.Count());
		Assert.AreEqual("Single", existingSingle!.Last().Value);
		Assert.AreEqual("Layer", existingLayer!.Last().Value);
		Assert.AreEqual("Leaf", existingLeaf!.Last().Value);
		Assert.AreEqual("Leaf2", existingLeaf2!.Last().Value);
		Assert.AreEqual("Deep1", existingDeep1!.Last().Value);
		Assert.AreEqual("Deeper", existingDeep2!.Last().Value);
		Assert.AreEqual("SINGLELAYER", existingSingle!.Last().LongName);
		Assert.AreEqual("TWO`LAYERS", existingLayer!.Last().LongName);
		Assert.AreEqual("TWO`LEAVES", existingLeaf!.Last().LongName);
		Assert.AreEqual("TWO`LEAVES2", existingLeaf2!.Last().LongName);
		Assert.AreEqual("THREE`LAYERS`DEEP", existingDeep1!.Last().LongName);
		Assert.AreEqual("THREE`LAYERS`DEEP2", existingDeep2!.Last().LongName);
		Assert.AreEqual("THREE`LAYERS", existingDeep1!.Skip(1).First().LongName);
		Assert.AreEqual("THREE`LAYERS", existingDeep2!.Skip(1).First().LongName);

		var attributes = obj.Object()!.Attributes();
		CollectionAssert.AllItemsAreInstancesOfType(attributes.ToList(), typeof(SharpAttribute));
		CollectionAssert.AllItemsAreNotNull(attributes.ToList());
	}
}