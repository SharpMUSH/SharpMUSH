using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class BuildingCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[DependsOn<GeneralCommandTests>]
	public async ValueTask CreateObject()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CreateObject - Test Object"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObject - Test Object");
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	public async ValueTask CreateObjectWithCost()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CreateObjectWithCost - Test Object=10"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObjectWithCost - Test Object");
	}

	[Test]
	[DependsOn(nameof(CreateObjectWithCost))]
	public async ValueTask DoDigForCommandListCheck()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Get the executor's current location to use in the assertion
		var currentLocation = await Parser.FunctionParse(MModule.single("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message!.ToPlainText()!);

		var newRoom = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dig DoDigTestRoom=DoDigTestExit;DoDigTestExitAlias,DoDigTestExitBack;DoDigTestExitAliasBack"));

		var newDb = DBRef.Parse(newRoom.Message!.ToPlainText()!);

		// Use unique room name in assertions to avoid pollution from other tests
		await NotifyService
			.Received()
			.Notify(executor, Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, $"DoDigTestRoom created with room number {newDb.Number}")));
		await NotifyService
			.Received()
			.Notify(executor, Arg.Is<OneOf<MString, string>>(msg =>
				msg.Match(
					mstr => mstr.ToString().Contains($"Linked exit #{newDb.Number + 1}") && mstr.ToString().Contains($"#{newDb.Number}"),
					str => str.Contains($"Linked exit #{newDb.Number + 1}") && str.Contains($"#{newDb.Number}")
				)));
		await NotifyService
			.Received()
			.Notify(executor, "Trying to link...");
		await NotifyService
			.Received()
			.Notify(executor, Arg.Is<OneOf<MString, string>>(msg =>
				msg.Match(
					mstr => mstr.ToString().Contains($"Linked exit #{newDb.Number + 2}") && mstr.ToString().Contains($"#{currentLocationDbRef.Number}"),
					str => str.Contains($"Linked exit #{newDb.Number + 2}") && str.Contains($"#{currentLocationDbRef.Number}")
				)));
	}

	// Something is getting created before this one can trigger...
	[Test, DependsOn(nameof(DoDigForCommandListCheck))]
	public async ValueTask DoDigForCommandListCheck2()
	{
		// Get the executor's current location to use in the assertion
		var currentLocation = await Parser.FunctionParse(MModule.single("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message!.ToPlainText()!);

		var newRoom = await Parser.CommandListParse(MModule.single("@dig Foo Room={Exit;ExitAlias},{ExitBack;ExitAliasBack}"));

		var newDb = DBRef.Parse(newRoom!.Message!.ToPlainText()!);

		var executor = WebAppFactoryArg.ExecutorDBRef;

		// Match against the specific executor DBRef instead of Arg.Any<DBRef>() to verify
		// that notifications are sent to the correct recipient.
		await NotifyService
			.Received()
			.Notify(executor, $"Foo Room created with room number {newDb.Number}.");
		await NotifyService
			.Received()
			.Notify(executor, $"Linked exit #{newDb.Number + 1} to #{newDb.Number}");
		await NotifyService
			.Received()
			.Notify(executor, "Trying to link...");
		await NotifyService
			.Received()
			.Notify(executor, $"Linked exit #{newDb.Number + 2} to #{currentLocationDbRef.Number}");
	}


	[Test]
	[DependsOn(nameof(DoDigForCommandListCheck2))]
	public async Task DigAndMoveTest()
	{
		if (Parser is null) throw new Exception("Parser is null");

		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig NewRoom=Forward;F,Backward;B"));
		var initialRoom = (await Parser.FunctionParse(MModule.single("%l")))!.Message!.ToPlainText();
		await Parser.CommandParse(1, ConnectionService, MModule.single("goto Forward"));
		var newRoom = (await Parser.FunctionParse(MModule.single("%l")))!.Message!.ToPlainText();
		await Parser.CommandParse(1, ConnectionService, MModule.single("goto Backward"));
		var finalRoom = (await Parser.FunctionParse(MModule.single("%l")))!.Message!.ToPlainText();

		await Assert.That(initialRoom).Length().IsPositive();
		await Assert.That(initialRoom).IsEqualTo(finalRoom);
		await Assert.That(newRoom).IsNotEqualTo(initialRoom);
	}

	[Test]
	[DependsOn(nameof(DigAndMoveTest))]
	public async ValueTask NameObject()
	{
		// Create an object first, capturing the dbref to avoid ambiguous name lookup
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create DigAndMoveTest - Rename Test"));
		var newDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Rename it using dbref to avoid "#-2 I DON'T KNOW WHICH ONE YOU MEAN" ambiguity
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@name {newDbRef}=DigAndMoveTest - New Name"));

		var renamedObject = await Mediator.Send(new GetObjectNodeQuery(newDbRef));
		await Assert.That(renamedObject.Object()!.Name).IsEqualTo("DigAndMoveTest - New Name");
	}

	[Test]
	[DependsOn(nameof(NameObject))]
	public async ValueTask DigRoom()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig DigRoom - Test Room"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("DigRoom - Test Room");
	}

	[Test]
	[DependsOn(nameof(DigRoom))]
	public async ValueTask DigRoomWithExits()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Room With Exits=In;I,Out;O"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await NotifyService
			.Received()
			.Notify(executor, $"Room With Exits created with room number {newObject.Object()!.DBRef.Number}.");
	}

	[Test]
	[DependsOn(nameof(DigRoomWithExits))]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask LinkExit()
	{
		// Create room and exit with unique names
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig LinkExitTestRoom"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var exitResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@open LinkExitTestExit"));
		var exitDbRef = DBRef.Parse(exitResult.Message!.ToPlainText()!);

		// Link them
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@link {exitDbRef}={roomDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<DBRef>(), Arg.Is<OneOf<MString, string>>(msg =>
				msg.Match(
					mstr => mstr.ToString().Contains("Linked") && mstr.ToString().Contains($"#{exitDbRef.Number}") && mstr.ToString().Contains($"#{roomDbRef.Number}"),
					str => str.Contains("Linked") && str.Contains($"#{exitDbRef.Number}") && str.Contains($"#{roomDbRef.Number}")
				)));
	}

	[Test]
	[DependsOn(nameof(LinkExit))]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - NotifyService call count mismatch")]
	public async ValueTask CloneObject()
	{
		// Create an object with unique name
		var sourceResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CloneObjectTestSource"));
		var sourceDbRef = DBRef.Parse(sourceResult.Message!.ToPlainText()!);

		// Clone it
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@clone {sourceDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				msg.Match(
					mstr => mstr.ToString().Contains("Cloned") && mstr.ToString().Contains("CloneObjectTestSource"),
					str => str.Contains("Cloned") && str.Contains("CloneObjectTestSource")
				)));
	}

	[Test]
	public async ValueTask ParentSetAndGet()
	{
		// Create two objects
		var parentResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentTestObject"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		var childResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ChildTestObject"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		// Verify both objects exist
		var parentObj = await Mediator.Send(new GetObjectNodeQuery(parentDbRef));
		var childObj = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		await Assert.That(parentObj.IsNone).IsFalse();
		await Assert.That(childObj.IsNone).IsFalse();

		// Verify child has no parent initially
		var initialParent = await childObj.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(initialParent.IsNone).IsTrue();

		// Set parent using @parent command
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Verify parent was set by querying database directly
		var updatedChild = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var setParent = await updatedChild.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(setParent.IsNone).IsFalse();
		await Assert.That(setParent.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ParentSetAndGet))]
	public async ValueTask ParentUnset()
	{
		// Create two objects
		var parentResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentUnsetTest_Parent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		var childResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentUnsetTest_Child"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Verify parent was set
		var childWithParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentSet = await childWithParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentSet.IsNone).IsFalse();

		// Unset parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}=none"));

		// Verify parent was cleared
		var childNoParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentCleared = await childNoParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentCleared.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentUnset))]
	public async ValueTask ParentCycleDetection_DirectCycle()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create two objects A and B
		var objAResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CycleTest_A"));
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);

		var objBResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CycleTest_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);

		// Set A's parent to B
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objADbRef}={objBDbRef}"));

		// Verify A's parent is B
		var objA = await Mediator.Send(new GetObjectNodeQuery(objADbRef));
		var parentOfA = await objA.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfA.IsNone).IsFalse();
		await Assert.That(parentOfA.Known.Object().DBRef.Number).IsEqualTo(objBDbRef.Number);

		// Try to set B's parent to A (would create direct cycle: A -> B -> A)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objBDbRef}={objADbRef}"));

		// Verify B's parent was NOT set (cycle prevention)
		var objB = await Mediator.Send(new GetObjectNodeQuery(objBDbRef));
		var parentOfB = await objB.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfB.IsNone).IsTrue();

		// Verify notification was sent about the cycle
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "loop") || TestHelpers.MessageContains(s, "cycle") || TestHelpers.MessageContains(s, "circular")), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_DirectCycle))]
	public async ValueTask ParentCycleDetection_IndirectCycle()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create three objects A, B, and C
		var objAResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create IndirectCycle_A"));
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);

		var objBResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create IndirectCycle_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);

		var objCResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create IndirectCycle_C"));
		var objCDbRef = DBRef.Parse(objCResult.Message!.ToPlainText()!);

		// Create chain: A -> B -> C
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objADbRef}={objBDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objBDbRef}={objCDbRef}"));

		// Verify the chain is established
		var objA = await Mediator.Send(new GetObjectNodeQuery(objADbRef));
		var parentOfA = await objA.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfA.IsNone).IsFalse();
		await Assert.That(parentOfA.Known.Object().DBRef.Number).IsEqualTo(objBDbRef.Number);

		var objB = await Mediator.Send(new GetObjectNodeQuery(objBDbRef));
		var parentOfB = await objB.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfB.IsNone).IsFalse();
		await Assert.That(parentOfB.Known.Object().DBRef.Number).IsEqualTo(objCDbRef.Number);

		// Try to set C's parent to A (would create indirect cycle: A -> B -> C -> A)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objCDbRef}={objADbRef}"));

		// Verify C's parent was NOT set (cycle prevention)
		var objC = await Mediator.Send(new GetObjectNodeQuery(objCDbRef));
		var parentOfC = await objC.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfC.IsNone).IsTrue();

		// Verify notification was sent about the cycle
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "loop") || TestHelpers.MessageContains(s, "cycle") || TestHelpers.MessageContains(s, "circular")), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_IndirectCycle))]
	public async ValueTask ParentCycleDetection_SelfParent()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create object
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create SelfParentTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Try to set object as its own parent (self-cycle)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objDbRef}={objDbRef}"));

		// Verify parent was NOT set (self-cycle prevention)
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var parent = await obj.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parent.IsNone).IsTrue();

		// Verify notification was sent about the cycle
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "loop") || TestHelpers.MessageContains(s, "cycle") || TestHelpers.MessageContains(s, "circular") || TestHelpers.MessageContains(s, "itself")), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_SelfParent))]
	public async ValueTask ParentCycleDetection_LongChain()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a long chain of 5 objects
		var objDbRefs = new List<DBRef>();
		for (int i = 0; i < 5; i++)
		{
			var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create LongChain_{i}"));
			objDbRefs.Add(DBRef.Parse(result.Message!.ToPlainText()!));
		}

		// Create chain: 0 -> 1 -> 2 -> 3 -> 4
		for (int i = 0; i < 4; i++)
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objDbRefs[i]}={objDbRefs[i + 1]}"));
		}

		// Verify the chain is established
		for (int i = 0; i < 4; i++)
		{
			var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRefs[i]));
			var parent = await obj.Known.Object().Parent.WithCancellation(CancellationToken.None);
			await Assert.That(parent.IsNone).IsFalse();
			await Assert.That(parent.Known.Object().DBRef.Number).IsEqualTo(objDbRefs[i + 1].Number);
		}

		// Try to set 4's parent to 0 (would create long cycle: 0 -> 1 -> 2 -> 3 -> 4 -> 0)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objDbRefs[4]}={objDbRefs[0]}"));

		// Verify 4's parent was NOT set (cycle prevention)
		var obj4 = await Mediator.Send(new GetObjectNodeQuery(objDbRefs[4]));
		var parentOf4 = await obj4.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOf4.IsNone).IsTrue();

		// Verify notification was sent about the cycle
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "loop") || TestHelpers.MessageContains(s, "cycle") || TestHelpers.MessageContains(s, "circular")), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[DependsOn(nameof(CloneObject))]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented - replaced by ParentSetAndGet")]
	public async ValueTask SetParent()
	{
		// Create two objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Parent Object"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Child Object"));

		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single("@parent #9=#8"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[DependsOn(nameof(SetParent))]
	public async ValueTask ChownObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Chown Test"));

		// Change ownership (to self in this case)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chown #10=#1"));

		// Verify command executed without permission error
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "PERMISSION DENIED")));
	}

	[Test]
	[DependsOn(nameof(ChownObject))]
	public async ValueTask ChzoneObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create objects
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zone Object"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zoned Object"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set zone
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "Zoned")), Arg.Any<AnySharpObject>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[DependsOn(nameof(ChzoneObject))]
	public async ValueTask RecycleObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Recycle Test"));

		// Recycle it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@recycle #13"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "Marked for destruction")));
	}

	[Test]
	[DependsOn(nameof(RecycleObject))]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnlinkExit()
	{
		// Create and link an exit
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Unlink Room"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@open Unlink Exit=#14"));

		// Unlink it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@unlink Unlink Exit"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "Unlinked")));
	}

	[Test]
	public async ValueTask SetFlag()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set #1=MONITOR"));

		var one = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var onePlayer = one.AsPlayer;
		var flags = await onePlayer.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Any(x => x.Name == "MONITOR" || x.Name == "DEBUG")).IsTrue();
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask LockObject()
	{
		// Create a unique object for this test to avoid pollution
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create LockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock {objDbRef}=#TRUE"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Locked.");
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask UnlockObject()
	{
		// Create a unique object for this test to avoid pollution
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create UnlockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Lock first
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock {objDbRef}=#TRUE"));

		// Then unlock
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@unlock {objDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "Unlocked.");
	}

	/// <summary>
	/// Tests that @desc (and other @attribute commands) evaluate their argument before storing.
	/// This confirms PennMUSH-compatible behavior:
	/// - @desc me=[add(1,2)] should store "3" (not "[add(1,2)]")
	/// - look should display "3" (no re-evaluation)
	/// Using @desc to verify prefix matching correctly chooses DESCRIBE over DESCFORMAT (shorter match wins).
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_EvaluatesBeforeStoring()
	{
		// Create an object for testing
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create DescEvalTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Get the object for attribute service
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Use @desc with a function that should be evaluated
		// @desc should match DESCRIBE (not DESCFORMAT) due to length sorting in prefix matching
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=[add(1,2)]"));

		// Verify the notification shows it was set
		await NotifyService
			.Received()
			.Notify(Arg.Any<long>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "DESCRIBE") && TestHelpers.MessageContains(msg, "Set")), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());

		// Retrieve the attribute and verify the stored value is "3" (evaluated), not "[add(1,2)]"
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var descAttr = await attributeService.GetAttributeAsync(
			obj.Known, obj.Known, "DESCRIBE",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(descAttr.IsAttribute).IsTrue();
		var storedValue = descAttr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("3");
	}

	/// <summary>
	/// Tests that @desc (prefix) works and correctly matches DESCRIBE over DESCFORMAT.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_PrefixMatch_Works()
	{
		// Create an object for testing
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create DescPrefixMatchTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Get the object for attribute service
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Use @desc (prefix) - should match DESCRIBE, not DESCFORMAT, due to shorter name
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=Test description text"));

		// Verify the attribute was set
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var descAttr = await attributeService.GetAttributeAsync(
			obj.Known, obj.Known, "DESCRIBE",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(descAttr.IsAttribute).IsTrue();
		var storedValue = descAttr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("Test description text");
	}

	/// <summary>
	/// Tests that look displays the pre-evaluated DESCRIBE value without re-evaluating.
	/// If DESCRIBE contained "[mul(2,5)]" and was set via @desc, look should show "10".
	/// </summary>
	[Test]
	public async ValueTask Look_DisplaysStoredDescribe_NoReEvaluation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an object for testing
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create LookDescTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Use @desc with a function - this should be evaluated to "10"
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=[mul(2,5)]"));

		// Look at the object
		await Parser.CommandParse(1, ConnectionService, MModule.single($"look {objDbRef}"));

		// Verify look displayed "10" (the evaluated result of [mul(2,5)])
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "10")));
	}

	/// <summary>
	/// Tests error case: @desc with invalid target shows error notification.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_InvalidTarget_ShowsError()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Try to @desc an object that doesn't exist
		await Parser.CommandParse(1, ConnectionService, MModule.single("@desc #99999=test description"));

		// Verify error notification was sent ("I don't see that here." or similar)
		// The locate service sends the error with a sender parameter
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "don't see") ||
				TestHelpers.MessageContains(msg, "can't see") ||
				TestHelpers.MessageContains(msg, "not found") ||
				TestHelpers.MessageContains(msg, "Invalid") ||
				TestHelpers.MessageContains(msg, "No match")), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	/// <summary>
	/// Tests that @desc without = clears the attribute.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_MissingEquals_ClearsAttribute()
	{
		// Create an object first
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create DescClearTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set a description first
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=Initial description"));

		// Now clear it by using @desc without =
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}"));

		// Verify "Cleared" notification was sent
		await NotifyService
			.Received()
			.Notify(Arg.Any<long>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "Cleared")), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}
}
