using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Database;

public class ArangoDBTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task TestRoomZero()
	{
		var roomZero = (await Database.GetObjectNodeAsync(new DBRef(0))).AsRoom;

		await Assert.That(roomZero).IsTypeOf<SharpRoom>();
		await Assert.That(roomZero.Object.Name).IsEqualTo("Room Zero");
		await Assert.That(roomZero.Object.Key).IsEqualTo(0);
	}

	[Test]
	public async Task TestRoomTwo()
	{
		var masterRoom = (await Database.GetObjectNodeAsync(new DBRef(2))).AsRoom;

		await Assert.That(masterRoom).IsTypeOf<SharpRoom>();
		await Assert.That(masterRoom.Object.Name).IsEqualTo("Master Room");
		await Assert.That(masterRoom.Object.Key).IsEqualTo(2);
	}

	[Test]
	public async Task TestPlayerOne()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne).IsTypeOf<SharpPlayer>();
		await Assert.That(playerOne.Object.Name).IsEqualTo("God");
		await Assert.That(playerOne.Object.Key).IsEqualTo(1);
	}

	[Test]
	public async Task TestAncestorRoomThree()
	{
		var ancestor = (await Database.GetObjectNodeAsync(new DBRef(3))).AsRoom;
		await Assert.That(ancestor).IsTypeOf<SharpRoom>();
		await Assert.That(ancestor.Object.Name).IsEqualTo("Ancestor Room");
		await Assert.That(ancestor.Object.Key).IsEqualTo(3);
	}

	[Test]
	public async Task TestAncestorPlayerFour()
	{
		// Ancestor Player is a THING (decided: avoid a loginable player ancestor).
		var ancestor = (await Database.GetObjectNodeAsync(new DBRef(4))).AsThing;
		await Assert.That(ancestor).IsTypeOf<SharpThing>();
		await Assert.That(ancestor.Object.Name).IsEqualTo("Ancestor Player");
		await Assert.That(ancestor.Object.Key).IsEqualTo(4);
	}

	[Test]
	public async Task TestAncestorExitFive()
	{
		var ancestor = (await Database.GetObjectNodeAsync(new DBRef(5))).AsThing;
		await Assert.That(ancestor).IsTypeOf<SharpThing>();
		await Assert.That(ancestor.Object.Name).IsEqualTo("Ancestor Exit");
		await Assert.That(ancestor.Object.Key).IsEqualTo(5);
	}

	[Test]
	public async Task TestAncestorThingSix()
	{
		var ancestor = (await Database.GetObjectNodeAsync(new DBRef(6))).AsThing;
		await Assert.That(ancestor).IsTypeOf<SharpThing>();
		await Assert.That(ancestor.Object.Name).IsEqualTo("Ancestor Thing");
		await Assert.That(ancestor.Object.Key).IsEqualTo(6);
	}

	[Test]
	public async Task TestPackageManagerSeven()
	{
		// Package Manager displaced from #3 to #7 by the ancestor renumber.
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).AsPlayer;
		await Assert.That(pm).IsTypeOf<SharpPlayer>();
		await Assert.That(pm.Object.Name).IsEqualTo("Package Manager");
		await Assert.That(pm.Object.Key).IsEqualTo(7);
	}

	[Test]
	public async Task TestHttpHandlerEight()
	{
		var handler = (await Database.GetObjectNodeAsync(new DBRef(8))).AsThing;
		await Assert.That(handler).IsTypeOf<SharpThing>();
		await Assert.That(handler.Object.Name).IsEqualTo("HTTP Handler");
		await Assert.That(handler.Object.Key).IsEqualTo(8);
	}

	[Test]
	public async Task TestEventHandlerNine()
	{
		var handler = (await Database.GetObjectNodeAsync(new DBRef(9))).AsThing;
		await Assert.That(handler).IsTypeOf<SharpThing>();
		await Assert.That(handler.Object.Name).IsEqualTo("Event Handler");
		await Assert.That(handler.Object.Key).IsEqualTo(9);
	}

	[Test]
	[Explicit]
	[NotInParallel]
	public async Task FirstCreatedGameObjectIsTen()
	{
		// The standard slots occupy #0-#9; the first freely-created game object on a freshly migrated
		// database must land at #10. Use a staging copy so the assertion is deterministic regardless of
		// objects created by other tests sharing the live database.
		// Staging is fully exercised on Arango (mirrors StagingDatabaseTests); skip on other providers
		// where the in-memory test harness does not wire a staging endpoint.
		var provider = Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER");
		if (!string.IsNullOrEmpty(provider) && !string.Equals(provider, "arangodb", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		await using var staging = await Database.CreateStagingAsync();

		var god = (await staging.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var roomZero = (await staging.GetObjectNodeAsync(new DBRef(0))).AsRoom;

		var created = await staging.CreateThingAsync("Renumber Probe Thing", roomZero, god, roomZero);

		await Assert.That(created.Number).IsEqualTo(10);

		await staging.AbortAsync();
	}

	[Test]
	public async Task AncestorPlayerSeedsFormatAttributes()
	{
		// The Ancestor Player (#4) carries the default FORMAT`* render templates so a plain player
		// inherits them. Read them directly off #4.
		var ancestor = new DBRef(4);

		var say = await Database.GetAttributeAsync(ancestor, ["FORMAT", "SAY"]).ToArrayAsync();
		var pose = await Database.GetAttributeAsync(ancestor, ["FORMAT", "POSE"]).ToArrayAsync();
		var semipose = await Database.GetAttributeAsync(ancestor, ["FORMAT", "SEMIPOSE"]).ToArrayAsync();
		var emit = await Database.GetAttributeAsync(ancestor, ["FORMAT", "EMIT"]).ToArrayAsync();

		await Assert.That(say.Length).IsGreaterThan(0);
		await Assert.That(pose.Length).IsGreaterThan(0);
		await Assert.That(semipose.Length).IsGreaterThan(0);
		await Assert.That(emit.Length).IsGreaterThan(0);

		await Assert.That(say.Last().Value.ToPlainText()).Contains("You say");
		await Assert.That(emit.Last().Value.ToPlainText()).IsEqualTo("%0");
	}

	[Test]
	[Repeat(10)]
	[NotInParallel]
	public async Task SetAndOverrideAnAttribute()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = new DBRef(playerOne.Object.Key);

		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], MModule.single("Layer"), playerOne);
		var existingLayer = await (Database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToListAsync();

		await Assert.That(existingLayer.Last().Value.ToString()).IsEqualTo("Layer");

		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], MModule.single("Layer2"), playerOne);
		var overwrittenLayer = await (Database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToListAsync();

		await Assert.That(overwrittenLayer.Last().Value.ToString()).IsEqualTo("Layer2");
	}

	[Test]
	public async Task StoreAnsiInAttribute()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1)));
		var playerOneDBRef = playerOne.Object()!.DBRef;

		var ansiString = A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "red");
		await Database.SetAttributeAsync(playerOneDBRef, ["AnsiTest", "Layers"], ansiString, playerOne.AsPlayer);
		var existingLayer = await (Database.GetAttributeAsync(playerOneDBRef, ["AnsiTest", "Layers"]))!.ToListAsync();

		await Assert.That(existingLayer.Last().Value.ToString()).IsEquatableOrEqualTo(ansiString.ToString());
	}

	[Test]
	public async Task GetAllObjectFlags()
	{
		var flags = await Database.GetObjectFlagsAsync().ToArrayAsync();
		await Assert.That(flags.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task GetAllAttributeFlags()
	{
		var flags = await Database.GetAttributeFlagsAsync().ToArrayAsync();
		await Assert.That(flags.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task SettingAKnownAttributeSetsFlags()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDbRef = playerOne.Object.DBRef;
		await Database.SetAttributeAsync(playerOneDbRef, ["TZ"], MModule.single("America/Chicago"), playerOne);
		var result = Database.GetAttributeAsync(playerOneDbRef, ["TZ"]);
		var realResult = await result!.FirstOrDefaultAsync();

		await Assert.That(realResult).IsNotNull();
		await Assert.That(realResult!.Flags).IsNotNull();
		await Assert.That(realResult.Flags).Count().IsEqualTo(2);
		await Assert.That(realResult.Flags).Contains(x => x.Name == "no_command");
		await Assert.That(realResult.Flags).Contains(x => x.Name == "visual");
	}

	[Test]
	[NotInParallel]
	[Repeat(2)]
	public async Task SetAndGetAnAttribute()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		await Assert.That(playerOne).IsTypeOf<SharpPlayer>();
		await Assert.That(playerOne.Object.Name).IsEqualTo("God");
		await Assert.That(playerOne.Object.Key).IsEqualTo(1);

		var playerOneDBRef = new DBRef(playerOne.Object.Key);

		await Database.SetAttributeAsync(playerOneDBRef, ["SingleLayer"], MModule.single("Single"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two"], MModule.single("Twin"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Layers"], MModule.single("Layer"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves"], MModule.single("Leaf"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"], MModule.single("Leaf2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"], MModule.single("Deep1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"], MModule.single("Deeper"), playerOne);

		var existingSingle = await (Database.GetAttributeAsync(playerOneDBRef, ["SingleLayer"]))!.ToListAsync();
		var existingLayer = await (Database.GetAttributeAsync(playerOneDBRef, ["Two", "Layers"]))!.ToListAsync();
		var existingLeaf = await (Database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves"]))!.ToListAsync();
		var existingLeaf2 = await (Database.GetAttributeAsync(playerOneDBRef, ["Two", "Leaves2"]))!.ToListAsync();
		var existingDeep1 = await (Database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep"]))!.ToListAsync();
		var existingDeep2 = await (Database.GetAttributeAsync(playerOneDBRef, ["Three", "Layers", "Deep2"]))!.ToListAsync();

		var obj = await Database.GetObjectNodeAsync(playerOneDBRef);

		await Assert.That(existingSingle.Count).IsEqualTo(1);
		await Assert.That(existingLayer.Count).IsEqualTo(2);
		await Assert.That(existingLeaf.Count).IsEqualTo(2);
		await Assert.That(existingLeaf2.Count).IsEqualTo(2);
		await Assert.That(existingDeep1.Count).IsEqualTo(3);
		await Assert.That(existingDeep2.Count).IsEqualTo(3);
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

		var attributes = obj.Object()!.Attributes.Value;

		await foreach (var attribute in attributes)
		{
			await Assert.That(attribute)
				.IsTypeOf<SharpAttribute>()
				.And
				.IsNotNull();
		}
	}

	[Test]
	public async Task GetParentsAsync_ReturnsFullParentChain()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;

		var parent1Dbref = await Database.CreateRoomAsync("Parent1", playerOne);
		var parent1 = (await Database.GetObjectNodeAsync(parent1Dbref)).AsRoom;

		var parent2Dbref = await Database.CreateRoomAsync("Parent2", playerOne);
		var parent2 = (await Database.GetObjectNodeAsync(parent2Dbref)).AsRoom;
		await Database.SetObjectParent(parent2, parent1);

		var childDbref = await Database.CreateRoomAsync("ChildRoom", playerOne);
		var child = (await Database.GetObjectNodeAsync(childDbref)).AsRoom;
		await Database.SetObjectParent(child, parent2);

		// Get the full parent chain using Object ID (not Room ID)
		var parents = await (Database.GetParentsAsync(child.Object.Id!, CancellationToken.None)).ToListAsync();

		await Assert.That(parents).Count().IsEqualTo(2);
		await Assert.That(parents[0].Key).IsEqualTo(parent2.Object.Key);
		await Assert.That(parents[1].Key).IsEqualTo(parent1.Object.Key);
	}
}