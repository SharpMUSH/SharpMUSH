using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
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
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create CreateObject - Test Object"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText());
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObject - Test Object");
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	public async ValueTask CreateObjectWithCost()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create CreateObjectWithCost - Test Object=10"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText());
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObjectWithCost - Test Object");
	}

	[Test]
	[DependsOn(nameof(CreateObjectWithCost))]
	public async ValueTask DoDigForCommandListCheck()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var currentLocation = await Parser.FunctionParse(MarkupText.Plain("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message!.ToPlainText());

		var newRoom = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@dig DoDigTestRoom=DoDigTestExit;DoDigTestExitAlias,DoDigTestExitBack;DoDigTestExitAliasBack"));

		var newDb = DBRef.Parse(newRoom.Message!.ToPlainText());

		// Use unique room name in assertions to avoid pollution from other tests
		await NotifyService
			.Received(1)
			.Notify(executor, Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"DoDigTestRoom created with room number {newDb.Number}.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(executor, Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"Linked exit #{newDb.Number + 1} to #{newDb.Number}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(executor, "Trying to link...", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(executor, Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"Linked exit #{newDb.Number + 2} to #{currentLocationDbRef.Number}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// Something is getting created before this one can trigger...
	[Test, DependsOn(nameof(DoDigForCommandListCheck))]
	public async ValueTask DoDigForCommandListCheck2()
	{
		var currentLocation = await Parser.FunctionParse(MarkupText.Plain("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message!.ToPlainText());

		var newRoom = await Parser.CommandListParse(MarkupText.Plain("@dig Foo Room={Exit;ExitAlias},{ExitBack;ExitAliasBack}"));

		var newDb = DBRef.Parse(newRoom!.Message!.ToPlainText());

		var executor = WebAppFactoryArg.ExecutorDBRef;

		// Match against the specific executor DBRef instead of Arg.Any<DBRef>() to verify
		// that notifications are sent to the correct recipient.
		await NotifyService
			.Received(1)
			.Notify(executor, $"Foo Room created with room number {newDb.Number}.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(executor, $"Linked exit #{newDb.Number + 1} to #{newDb.Number}", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(executor, "Trying to link...", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(executor, $"Linked exit #{newDb.Number + 2} to #{currentLocationDbRef.Number}", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}


	[Test]
	[DependsOn(nameof(DoDigForCommandListCheck2))]
	public async Task DigAndMoveTest()
	{
		if (Parser is null) throw new Exception("Parser is null");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dig NewRoom=Forward;F,Backward;B"));
		var initialRoom = (await Parser.FunctionParse(MarkupText.Plain("%l")))!.Message!.ToPlainText();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("goto Forward"));
		var newRoom = (await Parser.FunctionParse(MarkupText.Plain("%l")))!.Message!.ToPlainText();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("goto Backward"));
		var finalRoom = (await Parser.FunctionParse(MarkupText.Plain("%l")))!.Message!.ToPlainText();

		await Assert.That(initialRoom).Length().IsPositive();
		await Assert.That(initialRoom).IsEqualTo(finalRoom);
		await Assert.That(newRoom).IsNotEqualTo(initialRoom);
	}

	[Test]
	[DependsOn(nameof(DigAndMoveTest))]
	public async ValueTask NameObject()
	{
		// Create an object first, capturing the dbref to avoid ambiguous name lookup
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create DigAndMoveTest - Rename Test"));
		var newDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Rename it using dbref to avoid "#-2 I DON'T KNOW WHICH ONE YOU MEAN" ambiguity
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@name {newDbRef}=DigAndMoveTest - New Name"));

		var renamedObject = await Mediator.Send(new GetObjectNodeQuery(newDbRef));
		await Assert.That(renamedObject.Object()!.Name).IsEqualTo("DigAndMoveTest - New Name");
	}

	[Test]
	[DependsOn(nameof(NameObject))]
	public async ValueTask DigRoom()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dig DigRoom - Test Room"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("DigRoom - Test Room");
	}

	[Test]
	[DependsOn(nameof(DigRoom))]
	public async ValueTask DigRoomWithExits()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dig Room With Exits=In;I,Out;O"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await NotifyService
			.Received(1)
			.Notify(executor, $"Room With Exits created with room number {newObject.Object()!.DBRef.Number}.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(DigRoomWithExits))]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask LinkExit()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var roomResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dig LinkExitTestRoom"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var exitResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@open LinkExitTestExit"));
		var exitDbRef = DBRef.Parse(exitResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@link {exitDbRef}={roomDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(executor, Arg.Is<OneOf<MString, string>>(msg =>
				msg.Match(
					mstr => mstr.ToString().Contains("Linked") && mstr.ToString().Contains($"#{exitDbRef.Number}") && mstr.ToString().Contains($"#{roomDbRef.Number}"),
					str => str.Contains("Linked") && str.Contains($"#{exitDbRef.Number}") && str.Contains($"#{roomDbRef.Number}")
				)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(LinkExit))]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - NotifyService call count mismatch")]
	public async ValueTask CloneObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var sourceResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create CloneObjectTestSource"));
		var sourceDbRef = DBRef.Parse(sourceResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clone {sourceDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				msg.Match(
					mstr => mstr.ToString().Contains("Cloned") && mstr.ToString().Contains("CloneObjectTestSource"),
					str => str.Contains("Cloned") && str.Contains("CloneObjectTestSource")
				)), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ParentSetAndGet()
	{
		var parentResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create ParentTestObject"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		var childResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create ChildTestObject"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		var parentObj = await Mediator.Send(new GetObjectNodeQuery(parentDbRef));
		var childObj = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		await Assert.That(parentObj.IsNone).IsFalse();
		await Assert.That(childObj.IsNone).IsFalse();

		var initialParent = await childObj.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(initialParent.IsNone).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var updatedChild = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var setParent = await updatedChild.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(setParent.IsNone).IsFalse();
		await Assert.That(setParent.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ParentSetAndGet))]
	public async ValueTask ParentUnset()
	{
		var parentResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create ParentUnsetTest_Parent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		var childResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create ParentUnsetTest_Child"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var childWithParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentSet = await childWithParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentSet.IsNone).IsFalse();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childDbRef}=none"));

		var childNoParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentCleared = await childNoParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentCleared.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentUnset))]
	public async ValueTask ParentCycleDetection_DirectCycle()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objAResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create CycleTest_A"));
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);

		var objBResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create CycleTest_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objADbRef}={objBDbRef}"));

		var objA = await Mediator.Send(new GetObjectNodeQuery(objADbRef));
		var parentOfA = await objA.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfA.IsNone).IsFalse();
		await Assert.That(parentOfA.Known.Object().DBRef.Number).IsEqualTo(objBDbRef.Number);

		// Try to set B's parent to A (would create direct cycle: A -> B -> A)
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objBDbRef}={objADbRef}"));

		var objB = await Mediator.Send(new GetObjectNodeQuery(objBDbRef));
		var parentOfB = await objB.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfB.IsNone).IsTrue();

		// Cycle through the chain (A -> B, then B -> A attempted) - Penn's "You are not allowed to
		// be your own ancestor!" (src/set.c:1477), not the self-reference message.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CyclicAncestor), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_DirectCycle))]
	public async ValueTask ParentCycleDetection_IndirectCycle()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objAResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create IndirectCycle_A"));
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);

		var objBResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create IndirectCycle_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);

		var objCResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create IndirectCycle_C"));
		var objCDbRef = DBRef.Parse(objCResult.Message!.ToPlainText()!);

		// Create chain: A -> B -> C
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objADbRef}={objBDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objBDbRef}={objCDbRef}"));

		var objA = await Mediator.Send(new GetObjectNodeQuery(objADbRef));
		var parentOfA = await objA.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfA.IsNone).IsFalse();
		await Assert.That(parentOfA.Known.Object().DBRef.Number).IsEqualTo(objBDbRef.Number);

		var objB = await Mediator.Send(new GetObjectNodeQuery(objBDbRef));
		var parentOfB = await objB.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfB.IsNone).IsFalse();
		await Assert.That(parentOfB.Known.Object().DBRef.Number).IsEqualTo(objCDbRef.Number);

		// Try to set C's parent to A (would create indirect cycle: A -> B -> C -> A)
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objCDbRef}={objADbRef}"));

		var objC = await Mediator.Send(new GetObjectNodeQuery(objCDbRef));
		var parentOfC = await objC.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfC.IsNone).IsTrue();

		// Indirect cycle (A -> B -> C, then C -> A attempted) - same Penn message as a direct cycle.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CyclicAncestor), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_IndirectCycle))]
	public async ValueTask ParentCycleDetection_SelfParent()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create SelfParentTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Try to set object as its own parent (self-cycle)
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objDbRef}={objDbRef}"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var parent = await obj.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parent.IsNone).IsTrue();

		// Self-reference (@parent obj=obj) - Penn's "A thing cannot be its own ancestor!"
		// (src/set.c:1432), distinct from the cycle-through-chain message above.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SelfAncestor), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_SelfParent))]
	public async ValueTask ParentCycleDetection_LongChain()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRefs = new List<DBRef>();
		for (int i = 0; i < 5; i++)
		{
			var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create LongChain_{i}"));
			objDbRefs.Add(DBRef.Parse(result.Message!.ToPlainText()!));
		}

		// Create chain: 0 -> 1 -> 2 -> 3 -> 4
		for (int i = 0; i < 4; i++)
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objDbRefs[i]}={objDbRefs[i + 1]}"));
		}

		for (int i = 0; i < 4; i++)
		{
			var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRefs[i]));
			var parent = await obj.Known.Object().Parent.WithCancellation(CancellationToken.None);
			await Assert.That(parent.IsNone).IsFalse();
			await Assert.That(parent.Known.Object().DBRef.Number).IsEqualTo(objDbRefs[i + 1].Number);
		}

		// Try to set 4's parent to 0 (would create long cycle: 0 -> 1 -> 2 -> 3 -> 4 -> 0)
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {objDbRefs[4]}={objDbRefs[0]}"));

		var obj4 = await Mediator.Send(new GetObjectNodeQuery(objDbRefs[4]));
		var parentOf4 = await obj4.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOf4.IsNone).IsTrue();

		// 5-object cycle wrapping back on itself - still a cycle-through-chain, not self-reference.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CyclicAncestor), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(CloneObject))]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented - replaced by ParentSetAndGet")]
	public async ValueTask SetParent()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Parent Object"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Child Object"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@parent #9=#8"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Parent set."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(SetParent))]
	public async ValueTask ChownObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Chown Test"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@chown #10=#1"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Permission denied.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(ChownObject))]
	public async ValueTask ChzoneObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Zone Object"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create Zoned Object"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		// NotifyLocalized sends "Zone changed." via the ZoneChanged key
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ChzoneObject))]
	public async ValueTask RecycleObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an object, capture its DBRef so we don't rely on hardcoded #13
		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create RecycleTest_Unique"));
		var recycleDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@recycle {recycleDbRef}"));

		// Implementation sends: string.Format(ObjectScheduledDestroyedFormat, name)
		// = "RecycleTest_Unique is scheduled to be destroyed."
		await NotifyService
			.Received(1)
			.Notify(executor, "RecycleTest_Unique is scheduled to be destroyed.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask UnlinkExit()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		var roomName = TestIsolationHelpers.GenerateUniqueName("UnlinkRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("UnlinkExit");
		var openResult = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@open {exitName}={roomDbRef}"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@unlink {exitName}"));

		DBRef.TryParse(openResult.Message!.ToPlainText()!.Trim(), out var exitRef);
		var exit = await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value));
		var destination = await exit.AsExit.Home.WithCancellation(CancellationToken.None);

		await Assert.That(destination.IsNone).IsTrue();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.UnlinkedExit), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask SetFlag()
	{
		// Create a unique thing to set the flag on, instead of modifying shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetFlagTest");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thingDbRef}=MONITOR"));

		var thing = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var thingObj = thing.AsThing;
		var flags = await thingObj.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Any(x => x.Name == "MONITOR")).IsTrue();
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask LockObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a unique object for this test to avoid pollution
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create LockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock {objDbRef}=#TRUE"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Locked."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask UnlockObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a unique object for this test to avoid pollution
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create UnlockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@lock {objDbRef}=#TRUE"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@unlock {objDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Unlocked."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Tests that @desc (and other @attribute commands) store their argument without evaluating it.
	/// PennMUSH defers evaluation until the attribute is used.
	/// Using @desc to verify prefix matching correctly chooses DESCRIBE over DESCFORMAT (shorter match wins).
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_StoresContentsWithoutEvaluation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create DescEvalTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// @desc should match DESCRIBE (not DESCFORMAT) due to length sorting in prefix matching
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=[add(47119,82)]"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeSet))).IsTrue();

		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var descAttr = await attributeService.GetAttributeAsync(
			obj.Known, obj.Known, "DESCRIBE",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(descAttr.IsAttribute).IsTrue();
		var storedValue = descAttr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("[add(47119,82)]");
	}

	/// <summary>
	/// Tests that @desc (prefix) works and correctly matches DESCRIBE over DESCFORMAT.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_PrefixMatch_Works()
	{
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create DescPrefixMatchTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Use @desc (prefix) - should match DESCRIBE, not DESCFORMAT, due to shorter name
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=Test description text"));

		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var descAttr = await attributeService.GetAttributeAsync(
			obj.Known, obj.Known, "DESCRIBE",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(descAttr.IsAttribute).IsTrue();
		var storedValue = descAttr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("Test description text");
	}

	/// <summary>
	/// Tests that look displays a literal DESCRIBE value.
	/// </summary>
	[Test]
	public async ValueTask Look_DisplaysLiteralDescribe()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create LookDescTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Use @desc with a unique literal value to verify look displays it unchanged.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=LookDesc_UniqueTestValue_38471"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"look {objDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "LookDesc_UniqueTestValue_38471")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Looking at the room you are in shows its @describe run through @descformat.
	/// Rooms always use @describe/@descformat, never the @idescribe path (help @idescribe:
	/// "It's only used for players and things; rooms and exits always use @describe").
	/// </summary>
	[Test]
	public async ValueTask Look_Room_ShowsDescriptionThroughDescFormat()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ldf");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookRoomDF{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomDF_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@desc here=roomdesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&DESCFORMAT here=[ucstr(%0)]"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"ROOMDESC_{token.ToUpper()}")), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Looking at a room evaluates @describe, then passes the evaluated text to @descformat as %0.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_EvaluatesDescribeBeforePassingItToDescFormat()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lde");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookRoomEval{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomEval_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));

		var room = (await Mediator.Send(new GetObjectNodeQuery(roomDbRef))).Known;
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		await attributeService.SetAttributeAsync(playerObject, room, "DESCRIBE", MarkupText.Plain("[add(47119,82)]"));
		await attributeService.SetAttributeAsync(playerObject, room, "DESCFORMAT", MarkupText.Plain($"evaluated_{token}:%0"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"evaluated_{token}:47201")), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Looking at a room queues its @adescribe action with the looker as enactor.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_TriggersAdescribe()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lad");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookRoomAdesc{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomAdesc_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@adesc here=@pemit %#=adesc_{token}_from_%!"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var expectedMessage = $"adesc_{token}_from_#{roomDbRef.Number}";
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, expectedMessage);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, expectedMessage)), TestHelpers.MatchingObject(roomDbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A halted room cannot run its @adescribe action when viewed.
	/// </summary>
	[Test]
	public async ValueTask Look_HaltedRoom_DoesNotTriggerAdescribe()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lhra");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookHaltedRoom{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@dig HaltedRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wait 0=@pemit me=teleport_barrier_{token}"));
		await WebAppFactoryArg.Notifications.WaitForDeliveryAsync(
			player.DbRef, $"teleport_barrier_{token}", player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@adesc {roomDbRef}=@pemit %#=halted_adesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@set {roomDbRef}=HALT"));

		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wait 0=@pemit me=look_barrier_{token}"));
		await WebAppFactoryArg.Notifications.WaitForDeliveryAsync(
			player.DbRef, $"look_barrier_{token}", player.DbRef);
		var deliveries = WebAppFactoryArg.Notifications.DeliveriesFor(player.DbRef).Skip(before).ToList();

		await Assert.That(deliveries.Any(delivery => delivery.Message.Contains($"look_barrier_{token}"))).IsTrue();
		await Assert.That(deliveries.Any(delivery =>
			delivery.Sender == roomDbRef && delivery.Message.Contains($"halted_adesc_{token}"))).IsFalse();
	}

	/// <summary>
	/// A non-owner sees inherited @describe and @descformat output even though the format attribute
	/// is not readable by the viewer.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_UsesInheritedDescriptionAndPrivateFormatForNonOwner()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lip");
		var parentResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig LookParent_{token}"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!.Trim());
		var childResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig LookChild_{token}"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!.Trim());
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc {parentDbRef}=inherited_[add(20,22)]"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&DESCFORMAT {parentDbRef}=parent_{token}:%0"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/DESCRIBE=!visual"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/DESCFORMAT=!visual"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookInherit{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={childDbRef}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"parent_{token}:inherited_42")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// The inside-description path applies the same parent and permission rules as @describe.
	/// </summary>
	[Test]
	public async ValueTask Look_InsideThing_UsesInheritedPrivateIdescribeAndFormatForNonOwner()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("liip");
		var parentResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create InsideParent_{token}"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!.Trim());
		var childResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create InsideChild_{token}"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!.Trim());
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&IDESCRIBE {parentDbRef}=inside_[add(20,22)]"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&IDESCFORMAT {parentDbRef}=inner_{token}:%0"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/IDESCRIBE=!visual"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/IDESCFORMAT=!visual"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookInside{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={childDbRef}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"inner_{token}:inside_42")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A forced look evaluates descriptions and actions with the forced player, not the forcer, as enactor.
	/// </summary>
	[Test]
	public async ValueTask Look_ForcedPlayer_IsEnactorForDescriptionFormatAndAction()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lfi");
		var roomResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig ForceRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!.Trim());
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"ForceLook{token}");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc {roomDbRef}=desc:%#:%@"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&DESCFORMAT {roomDbRef}=format_{token}:%#:%@:%0"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {roomDbRef}/DESCFORMAT=visual"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@adesc {roomDbRef}=@pemit %#=action_{token}:%#:%@:%!"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@force {player.DbRef}=look"));

		var playerRef = $"#{player.DbRef.Number}";
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef,
			$"action_{token}:{playerRef}:{playerRef}:#{roomDbRef.Number}");
		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg,
				$"format_{token}:{playerRef}:{playerRef}:desc:{playerRef}:{playerRef}")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A present but empty @describe is real description output and supplies an empty %0 to @descformat.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_EmptyDescription_PassesEmptyValueToDescFormat()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("led");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookEmpty{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig EmptyRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@desc here="));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&DESCFORMAT here=empty_{token}:%+:%0:end"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"empty_{token}:1::end")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A present but empty @descformat suppresses an otherwise non-empty description.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_EmptyDescFormatSuppressesDescription()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lefs");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookEmptyFormat{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig EmptyFormatRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@desc here=visible_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&DESCFORMAT here="));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));
		var messages = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(messages.Any(message => message.Contains($"visible_{token}"))).IsFalse();
	}

	/// <summary>
	/// The action queued by look executes the value retrieved during look even if the attribute changes
	/// before the nested queue entry runs.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_AdescribeQueueUsesRetrievedValueSnapshot()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("las");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookSnapshot{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig SnapshotRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@adesc here=@pemit %#=snapshot_old_{token}"));

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@dolist 1={{look; @adesc here=@pemit %#=snapshot_new_{token}}}"));

		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, $"snapshot_old_{token}");
		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"snapshot_old_{token}")),
			TestHelpers.MatchingObject(roomDbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Looking at a room with no @describe still evaluates @descformat, without supplying %0.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_NoDescription_UsesDescFormatWithoutArgument()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lnd");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookRoomND{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomND_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message!.ToPlainText()!.Trim());
		// Keep the teleport's queued auto-look out of the explicit look's notification window.
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel/silent me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&DESCFORMAT here=formatted_{token}:%+:%0:end"));

		var expectedMessage = $"formatted_{token}:0::end";
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, expectedMessage);
		var messages = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(messages).Contains(expectedMessage);
		await Assert.That(messages).DoesNotContain("You see nothing special.");
	}

	/// <summary>
	/// With neither @describe nor @descformat, look retains the default description.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_NoDescriptionOrFormat_ShowsDefault()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lndf");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookRoomDefault{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomDefault_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message!.ToPlainText()!.Trim());
		// Keep the teleport's queued auto-look out of the explicit look's notification window.
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel/silent me={roomDbRef}"));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));
		var messages = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(messages).Contains("You see nothing special.");
	}

	/// <summary>
	/// Looking while inside a thing with no @idescribe falls back to @describe and
	/// @descformat (help @idescformat: "When no @idescribe is set, the @descformat (and
	/// @describe) attributes are used, even when someone looks inside").
	/// </summary>
	[Test]
	public async ValueTask Look_InsideThing_NoIdesc_FallsBackToDescribeAndDescFormat()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lit");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookThing{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var objResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create ThingIT_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=thingdesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&DESCFORMAT {objDbRef}=[ucstr(%0)]"));
		// @create puts the thing in inventory; drop it so entering it isn't a containment loop.
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop {objDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={objDbRef}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		// The @descformat evaluation is queued, so it can land after CommandParse returns.
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, $"THINGDESC_{token.ToUpper()}");

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"THINGDESC_{token.ToUpper()}")), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Inside a thing, a present @idescformat formats the fallback @describe even without @idescribe,
	/// and that branch triggers neither outside nor inside description actions.
	/// </summary>
	[Test]
	public async ValueTask Look_InsideThing_NoIdesc_UsesIdescFormatWithoutDescriptionActions()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("liifa");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookIdescFallback{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var objResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create ThingIF_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=outer_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&DESCFORMAT {objDbRef}=wrong_format_{token}:%0"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&IDESCFORMAT {objDbRef}=inside_format_{token}:%+:%0"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@adesc {objDbRef}=@pemit %#=wrong_adesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&AIDESCRIBE {objDbRef}=@pemit %#=wrong_aidesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop {objDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={objDbRef}"));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wait 0=@pemit me=look_barrier_{token}"));
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, $"look_barrier_{token}");
		var messages = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(messages).Contains($"inside_format_{token}:1:outer_{token}");
		await Assert.That(messages.Any(message => message.Contains($"wrong_format_{token}"))).IsFalse();
		await Assert.That(messages.Any(message => message.Contains($"wrong_adesc_{token}"))).IsFalse();
		await Assert.That(messages.Any(message => message.Contains($"wrong_aidesc_{token}"))).IsFalse();
	}

	/// <summary>
	/// Looking while inside a thing WITH an @idescribe uses it, run through @idescformat.
	/// </summary>
	[Test]
	public async ValueTask Look_InsideThing_WithIdesc_UsesIdescThroughIdescFormat()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lii");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookIdesc{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var objResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create ThingII_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=outsidedesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&IDESCRIBE {objDbRef}=insidedesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&IDESCFORMAT {objDbRef}=[ucstr(%0)]"));
		// @create puts the thing in inventory; drop it so entering it isn't a containment loop.
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop {objDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={objDbRef}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		// The @idescformat evaluation is queued, so it can land after CommandParse returns.
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, $"INSIDEDESC_{token.ToUpper()}");

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"INSIDEDESC_{token.ToUpper()}")), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A halted container cannot run its @aidescribe action when viewed from inside.
	/// </summary>
	[Test]
	public async ValueTask Look_HaltedContainer_DoesNotTriggerAidescribe()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lhca");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookHaltedContainer{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var objResult = await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@create HaltedContainer_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&IDESCRIBE {objDbRef}=inside_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&AIDESCRIBE {objDbRef}=@pemit %#=halted_aidesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@set {objDbRef}=HALT"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop {objDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={objDbRef}"));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@wait 0=@pemit me=look_barrier_{token}"));
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, $"look_barrier_{token}");
		var messages = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(messages).Contains($"look_barrier_{token}");
		await Assert.That(messages.Any(message => message.Contains($"halted_aidesc_{token}"))).IsFalse();
	}

	/// <summary>
	/// An inside description is not passed through the outside description format when
	/// no inside description format is present.
	/// </summary>
	[Test]
	public async ValueTask Look_InsideThing_WithIdescAndNoIdescFormat_DoesNotUseDescFormat()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("liinf");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"LookIdescNoFormat{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var objResult = await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@create ThingIINF_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&IDESCRIBE {objDbRef}=inside_raw_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&DESCFORMAT {objDbRef}=wrong_outside_format_{token}:%0"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop {objDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={objDbRef}"));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));
		var messages = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(messages).Contains($"inside_raw_{token}");
		await Assert.That(messages.Any(message => message.Contains($"wrong_outside_format_{token}"))).IsFalse();
	}

	/// <summary>
	/// Tests error case: @desc with invalid target shows error notification.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_InvalidTarget_ShowsError()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DescribeCommand");
		var testParser = WebAppFactoryArg.CommandParserFor(testPlayer.DbRef, testPlayer.Handle);

		await testParser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@desc #99999=test description"));

		// do_set's failed match is match.c:485's "I can't see that here."; "I don't see that here." is
		// look.c/move.c's string, for the commands that match for themselves.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "I can't see that here.")), TestHelpers.MatchingObject(testPlayer.DbRef),
				INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Tests that @desc without = clears the attribute.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_MissingEquals_ClearsAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@create DescClearTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=Initial description"));

		// Now clear it by using @desc without =
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc {objDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeCleared))).IsTrue();
	}

	/// <summary>
	/// @clone copies attributes but strips privileged flags.
	/// PennMUSH testsidefx.t: clone.1-6
	/// </summary>
	[Test]
	public async ValueTask Clone_CopiesAttrs_StripsPrivilegedFlags()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("cln");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create ClnSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set ClnSrc_{token}=Wizard"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&FOO ClnSrc_{token}=blah_{token}"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clone ClnSrc_{token}=ClnCopy_{token}"));

		var flagResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think hasflag(ClnCopy_{token}, WIZARD)"));
		await Assert.That(flagResult.Message!.ToPlainText()!.Trim()).IsEqualTo("0");

		var attrResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think hasattr(ClnCopy_{token}, FOO)"));
		await Assert.That(attrResult.Message!.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// The configured creation defaults reach the object, on whichever provider the suite is running.
	/// The defaults are looked up through the flag store directly, and the two providers disagreed
	/// about resolving a name in the case the config supplies it, so this passed on Lightning and
	/// applied nothing at all on SurrealDB.
	/// </summary>
	[Test]
	public async ValueTask Create_AppliesTheConfiguredDefaultFlags()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("dflt");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create DfltThing_{token}"));

		var flagged = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"think hasflag(DfltThing_{token}, NO_COMMAND)"));

		await Assert.That(flagged.Message!.ToPlainText()!.Trim()).IsEqualTo("1")
			.Because("thing_flags ships as no_command, and a default nothing applies is not a default");
	}

	/// <summary>
	/// A clone is created through the same path as any other object, so it arrives carrying the
	/// configured creation defaults — NO_COMMAND among them. Copying only the flags the source has
	/// left that default in place on a source that had deliberately cleared it, and the `$`-commands
	/// copied onto the clone in the same breath then never ran.
	/// </summary>
	[Test]
	public async ValueTask Clone_DoesNotKeepACreationDefaultTheSourceCleared()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clnc");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create ClncSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set ClncSrc_{token}=!NO_COMMAND"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clone ClncSrc_{token}=ClncCopy_{token}"));

		var cloned = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"think hasflag(ClncCopy_{token}, NO_COMMAND)"));

		await Assert.That(cloned.Message!.ToPlainText()!.Trim()).IsEqualTo("0")
			.Because("the clone's flags are synchronised to the source, not unioned with the defaults");
	}

	/// <summary>A flag the source does have still reaches the clone.</summary>
	[Test]
	public async ValueTask Clone_KeepsANonDefaultFlagTheSourceHas()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clnk");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create ClnkSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set ClnkSrc_{token}=OPAQUE"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clone ClnkSrc_{token}=ClnkCopy_{token}"));

		var cloned = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"think hasflag(ClnkCopy_{token}, OPAQUE)"));

		await Assert.That(cloned.Message!.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// @clone/preserve copies privileged flags too.
	/// PennMUSH testsidefx.t: clone.7-8
	/// </summary>
	[Test]
	public async ValueTask Clone_Preserve_CopiesPrivilegedFlags()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clp");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create ClpSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set ClpSrc_{token}=Wizard"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clone/preserve ClpSrc_{token}=ClpCopy_{token}"));

		var flagResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think hasflag(ClpCopy_{token}, WIZARD)"));
		await Assert.That(flagResult.Message!.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// clone() function works like @clone; clone(..., preserve) like @clone/preserve.
	/// PennMUSH testsidefx.t: clone.9-12
	/// </summary>
	[Test]
	public async ValueTask CloneFunction_WithAndWithoutPreserve()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clf");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create ClfSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set ClfSrc_{token}=Wizard"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think clone(ClfSrc_{token}, ClfNP_{token})"));
		var npFlag = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think hasflag(ClfNP_{token}, WIZARD)"));
		await Assert.That(npFlag.Message!.ToPlainText()!.Trim()).IsEqualTo("0");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think clone(ClfSrc_{token}, ClfP_{token}, , preserve)"));
		var pFlag = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think hasflag(ClfP_{token}, WIZARD)"));
		await Assert.That(pFlag.Message!.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// @clone and clone() of non-existent object returns error.
	/// PennMUSH testsidefx.t: clone.13-14
	/// </summary>
	[Test]
	public async ValueTask Clone_NonExistentObject_Errors()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clx");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@clone NoSuchObj_{token}"));

		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think clone(NoSuchObj_{token})"));
		var output = result.Message!.ToPlainText()!.Trim();
		// SharpMUSH returns "#-1 NO MATCH" for failed locate
		await Assert.That(output).StartsWith("#-1");
	}

	/// <summary>
	/// The number half of a reference that may have been rendered as a full objid
	/// (<c>#N:creation</c>). <c>home()</c> answers with an objid; a dbref written into the command
	/// is bare, so both sides of a comparison go through this.
	/// </summary>
	private static string BareDbref(string reference)
	{
		var colon = reference.IndexOf(':');
		return colon < 0 ? reference : reference[..colon];
	}

	private async Task<string> HomeOf(DBRef reference)
	{
		var home = await Parser.FunctionParse(MarkupText.Plain($"[home(#{reference.Number})]"));
		return BareDbref(home!.Message!.ToPlainText().Trim());
	}

	/// <summary>
	/// <c>do_link</c> gates the home destination as well as the object (<c>src/create.c:404</c>):
	/// <c>!controls(player, room) &amp;&amp; !Abode(room)</c>. Any non-exit can be a home, so without
	/// this gate controlling the object alone would be enough to park it in a stranger's inventory.
	/// </summary>
	[Test]
	public async ValueTask LinkingAHomeNeedsControlOfTheDestinationOrAbode()
	{
		var linker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LinkGateOwner");
		var stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LinkGateStranger");

		var item = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LinkGateItem");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown #{item.Number}=#{linker.DbRef.Number}"));

		var abode = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("LinkGateAbode")}"));
		var abodeRoom = BareDbref(abode.Message!.ToPlainText().Trim());
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {abodeRoom}=ABODE"));

		var before = await HomeOf(item);

		// Neither controlled nor ABODE: refused, and the home is untouched.
		var recorder = WebAppFactoryArg.Notifications;
		var seen = recorder.CountFor(linker.DbRef);
		var refusal = await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"@link #{item.Number}=#{stranger.DbRef.Number}"));
		await Assert.That(refusal.Message!.ToPlainText().Trim()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);

		await Assert.That(await HomeOf(item)).IsEqualTo(before);
		await Assert.That(recorder.For(linker.DbRef).Skip(seen).Any(m => m == ErrorMessages.Notifications.PermissionDenied))
			.IsTrue();

		// ABODE without control is enough.
		await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"@link #{item.Number}={abodeRoom}"));
		await Assert.That(await HomeOf(item)).IsEqualTo(abodeRoom);

		// So is control without ABODE — a player is never ABODE, the flag is ROOM-only.
		await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"@link #{item.Number}=#{linker.DbRef.Number}"));
		await Assert.That(await HomeOf(item)).IsEqualTo($"#{linker.DbRef.Number}");
	}

	/// <summary>
	/// <c>link()</c> carries <c>do_link</c>'s destination gate too (<c>src/create.c:404</c>).
	/// </summary>
	[Test]
	public async ValueTask LinkFunctionHomeNeedsControlOfTheDestinationOrAbode()
	{
		var linker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LinkFnOwner");
		var stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LinkFnStranger");

		var item = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "LinkFnItem");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown #{item.Number}=#{linker.DbRef.Number}"));

		var before = await HomeOf(item);

		var refused = await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{item.Number}, #{stranger.DbRef.Number})"));
		await Assert.That(refused.Message!.ToPlainText().Trim()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await HomeOf(item)).IsEqualTo(before);

		var allowed = await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{item.Number}, #{linker.DbRef.Number})"));
		await Assert.That(allowed.Message!.ToPlainText().Trim()).IsEqualTo("1");
		await Assert.That(await HomeOf(item)).IsEqualTo($"#{linker.DbRef.Number}");
	}
}
