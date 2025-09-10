using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Database;

public class ArangoDBTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	
	[Test]
	public async Task TestRoomZero()
	{
		var roomZero = (await Database!.GetObjectNodeAsync(new DBRef(0))).AsRoom;

		await Assert.That(roomZero).IsTypeOf(typeof(SharpRoom));
		await Assert.That(roomZero!.Object!.Name).IsEqualTo("Room Zero");
		await Assert.That(roomZero!.Object!.Key).IsEqualTo(0);
	}

	[Test]
	public async Task TestRoomTwo()
	{
		var masterRoom = (await Database!.GetObjectNodeAsync(new DBRef(2))).AsRoom;

		await Assert.That(masterRoom).IsTypeOf(typeof(SharpRoom));
		await Assert.That(masterRoom!.Object!.Name).IsEqualTo("Master Room");
		await Assert.That(masterRoom!.Object!.Key).IsEqualTo(2);
	}

	[Test]
	public async Task TestPlayerOne()
	{
		var playerOne = (await Database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne).IsTypeOf<SharpPlayer>();
		await Assert.That(playerOne!.Object!.Name).IsEqualTo("God");
		await Assert.That(playerOne!.Object!.Key).IsEqualTo(1);
	}

	[Test]
	[Repeat(10)]
	public async Task SetAndOverrideAnAttribute()
	{
		var playerOne = (await Database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = new DBRef(playerOne!.Object.Key);

		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], MModule.single("Layer"), playerOne);
		var existingLayer = (await Database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToList();

		await Assert.That(existingLayer.Last().Value.ToString()).IsEqualTo("Layer");

		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], MModule.single("Layer2"), playerOne);
		var overwrittenLayer = (await Database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToList();

		await Assert.That(overwrittenLayer.Last().Value.ToString()).IsEqualTo("Layer2");
	}

	[Test]
	public async Task StoreAnsiInAttribute()
	{
		var playerOne = (await Database!.GetObjectNodeAsync(new DBRef(1)));
		var playerOneDBRef = playerOne.Object()!.DBRef;

		var ansiString = A.markupSingle(M.Create(foreground: ANSILibrary.StringExtensions.rgb(Color.Red)), "red");
		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], ansiString, playerOne.AsPlayer);
		var existingLayer = (await Database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!;

		await Assert.That(existingLayer.Last().Value.ToString()).IsEquatableOrEqualTo(ansiString.ToString());
	}

	[Test]
	public async Task GetAllObjectFlags()
	{
		var flags = await Database!.GetObjectFlagsAsync();
		await Assert.That(flags.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task GetAllAttributeFlags()
	{
		var flags = await Database!.GetAttributeFlagsAsync();
		await Assert.That(flags.Count).IsGreaterThan(0);
	}

	[Test]
	[NotInParallel]
	[Repeat(10)] // Exclusive Locks are needed first. Otherwise there will be write-write errors. 
	public async Task SetAndGetAnAttribute()
	{
		var playerOne = (await Database!.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne).IsTypeOf<SharpPlayer>();
		await Assert.That(playerOne.Object.Name).IsEqualTo("God");
		await Assert.That(playerOne.Object.Key).IsEqualTo(1);

		var playerOneDBRef = new DBRef(playerOne!.Object.Key);

		await Database.SetAttributeAsync(playerOneDBRef, ["SingleLayer"], MModule.single("Single"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two"], MModule.single("Twin"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], MModule.single("Layer"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves"], MModule.single("Leaf"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"], MModule.single("Leaf2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"], MModule.single("Deep1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"], MModule.single("Deeper"), playerOne);

		var existingSingle = (await Database.GetAttributeAsync(playerOneDBRef, ["SingleLayer"]))?.ToList();
		var existingLayer = (await Database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))?.ToList();
		var existingLeaf = (await Database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves"]))?.ToList();
		var existingLeaf2 = (await Database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"]))?.ToList();
		var existingDeep1 = (await Database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"]))?.ToList();
		var existingDeep2 = (await Database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"]))?.ToList();

		var obj = await Database!.GetObjectNodeAsync(playerOneDBRef);

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

		var attributes = await obj.Object()!.Attributes.WithCancellation(CancellationToken.None);

		foreach (var attribute in attributes)
		{
			await Assert.That(attribute)
				.IsTypeOf<SharpAttribute>()
				.And
				.IsNotNull();
		}
	}
}