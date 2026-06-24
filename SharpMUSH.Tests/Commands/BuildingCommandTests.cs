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
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CreateObject - Test Object"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText());
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObject - Test Object");
	}

	[Test]
	[DependsOn(nameof(CreateObject))]
	public async ValueTask CreateObjectWithCost()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CreateObjectWithCost - Test Object=10"));

		var newDb = DBRef.Parse(result.Message!.ToPlainText());
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObjectWithCost - Test Object");
	}

	[Test]
	[DependsOn(nameof(CreateObjectWithCost))]
	public async ValueTask DoDigForCommandListCheck()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var currentLocation = await Parser.FunctionParse(MModule.single("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message!.ToPlainText());

		var newRoom = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dig DoDigTestRoom=DoDigTestExit;DoDigTestExitAlias,DoDigTestExitBack;DoDigTestExitAliasBack"));

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
		var currentLocation = await Parser.FunctionParse(MModule.single("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message!.ToPlainText());

		var newRoom = await Parser.CommandListParse(MModule.single("@dig Foo Room={Exit;ExitAlias},{ExitBack;ExitAliasBack}"));

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
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig LinkExitTestRoom"));
		var roomDbRef = DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var exitResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@open LinkExitTestExit"));
		var exitDbRef = DBRef.Parse(exitResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@link {exitDbRef}={roomDbRef}"));

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
		var sourceResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CloneObjectTestSource"));
		var sourceDbRef = DBRef.Parse(sourceResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@clone {sourceDbRef}"));

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
		var parentResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentTestObject"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		var childResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ChildTestObject"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		var parentObj = await Mediator.Send(new GetObjectNodeQuery(parentDbRef));
		var childObj = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		await Assert.That(parentObj.IsNone).IsFalse();
		await Assert.That(childObj.IsNone).IsFalse();

		var initialParent = await childObj.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(initialParent.IsNone).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		var updatedChild = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var setParent = await updatedChild.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(setParent.IsNone).IsFalse();
		await Assert.That(setParent.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ParentSetAndGet))]
	public async ValueTask ParentUnset()
	{
		var parentResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentUnsetTest_Parent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		var childResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ParentUnsetTest_Child"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		var childWithParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentSet = await childWithParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentSet.IsNone).IsFalse();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}=none"));

		var childNoParent = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentCleared = await childNoParent.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentCleared.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentUnset))]
	public async ValueTask ParentCycleDetection_DirectCycle()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objAResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CycleTest_A"));
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);

		var objBResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create CycleTest_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objADbRef}={objBDbRef}"));

		var objA = await Mediator.Send(new GetObjectNodeQuery(objADbRef));
		var parentOfA = await objA.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfA.IsNone).IsFalse();
		await Assert.That(parentOfA.Known.Object().DBRef.Number).IsEqualTo(objBDbRef.Number);

		// Try to set B's parent to A (would create direct cycle: A -> B -> A)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objBDbRef}={objADbRef}"));

		var objB = await Mediator.Send(new GetObjectNodeQuery(objBDbRef));
		var parentOfB = await objB.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfB.IsNone).IsTrue();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ParentLoopCannotAdd), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_DirectCycle))]
	public async ValueTask ParentCycleDetection_IndirectCycle()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objAResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create IndirectCycle_A"));
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);

		var objBResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create IndirectCycle_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);

		var objCResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create IndirectCycle_C"));
		var objCDbRef = DBRef.Parse(objCResult.Message!.ToPlainText()!);

		// Create chain: A -> B -> C
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objADbRef}={objBDbRef}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objBDbRef}={objCDbRef}"));

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

		var objC = await Mediator.Send(new GetObjectNodeQuery(objCDbRef));
		var parentOfC = await objC.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfC.IsNone).IsTrue();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ParentLoopCannotAdd), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_IndirectCycle))]
	public async ValueTask ParentCycleDetection_SelfParent()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create SelfParentTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Try to set object as its own parent (self-cycle)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objDbRef}={objDbRef}"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var parent = await obj.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parent.IsNone).IsTrue();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ParentLoopCannotAdd), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ParentCycleDetection_SelfParent))]
	public async ValueTask ParentCycleDetection_LongChain()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
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

		for (int i = 0; i < 4; i++)
		{
			var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRefs[i]));
			var parent = await obj.Known.Object().Parent.WithCancellation(CancellationToken.None);
			await Assert.That(parent.IsNone).IsFalse();
			await Assert.That(parent.Known.Object().DBRef.Number).IsEqualTo(objDbRefs[i + 1].Number);
		}

		// Try to set 4's parent to 0 (would create long cycle: 0 -> 1 -> 2 -> 3 -> 4 -> 0)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {objDbRefs[4]}={objDbRefs[0]}"));

		var obj4 = await Mediator.Send(new GetObjectNodeQuery(objDbRefs[4]));
		var parentOf4 = await obj4.Known.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOf4.IsNone).IsTrue();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ParentLoopCannotAdd), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(CloneObject))]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented - replaced by ParentSetAndGet")]
	public async ValueTask SetParent()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Parent Object"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Child Object"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@parent #9=#8"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Parent set.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(SetParent))]
	public async ValueTask ChownObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Chown Test"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@chown #10=#1"));

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
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zone Object"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create Zoned Object"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// NotifyLocalized sends "Zone changed." via the ZoneChanged key
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(ChzoneObject))]
	public async ValueTask RecycleObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create an object, capture its DBRef so we don't rely on hardcoded #13
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create RecycleTest_Unique"));
		var recycleDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@recycle {recycleDbRef}"));

		// Implementation sends: string.Format(ObjectScheduledDestroyedFormat, name)
		// = "RecycleTest_Unique is scheduled to be destroyed."
		await NotifyService
			.Received(1)
			.Notify(executor, "RecycleTest_Unique is scheduled to be destroyed.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(RecycleObject))]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnlinkExit()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig Unlink Room"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@open Unlink Exit=#14"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@unlink Unlink Exit"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageContains(msg, "Unlinked")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask SetFlag()
	{
		// Create a unique thing to set the flag on, instead of modifying shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetFlagTest");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {thingDbRef}=MONITOR"));

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
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create LockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock {objDbRef}=#TRUE"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Locked.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask UnlockObject()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a unique object for this test to avoid pollution
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create UnlockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@lock {objDbRef}=#TRUE"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@unlock {objDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Unlocked.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create DescEvalTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// @desc should match DESCRIBE (not DESCFORMAT) due to length sorting in prefix matching
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=[add(47119,82)]"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AttributeSet))).IsTrue();

		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var descAttr = await attributeService.GetAttributeAsync(
			obj.Known, obj.Known, "DESCRIBE",
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(descAttr.IsAttribute).IsTrue();
		var storedValue = descAttr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("47201");
	}

	/// <summary>
	/// Tests that @desc (prefix) works and correctly matches DESCRIBE over DESCFORMAT.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_PrefixMatch_Works()
	{
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create DescPrefixMatchTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Use @desc (prefix) - should match DESCRIBE, not DESCFORMAT, due to shorter name
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=Test description text"));

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
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create LookDescTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Use @desc with a unique value - stored as-is (no function evaluation tested here)
		// to verify look displays the stored description without re-evaluation
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=LookDesc_UniqueTestValue_38471"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"look {objDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "LookDesc_UniqueTestValue_38471")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
		
		await testParser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@desc #99999=test description"));
	
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "I don't see that here.")), TestHelpers.MatchingObject(testPlayer.DbRef), 
				INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Tests that @desc without = clears the attribute.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_MissingEquals_ClearsAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create DescClearTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}=Initial description"));

		// Now clear it by using @desc without =
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@desc {objDbRef}"));

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

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create ClnSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set ClnSrc_{token}=Wizard"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO ClnSrc_{token}=blah_{token}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@clone ClnSrc_{token}=ClnCopy_{token}"));

		var flagResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"think hasflag(ClnCopy_{token}, WIZARD)"));
		await Assert.That(flagResult.Message!.ToPlainText()!.Trim()).IsEqualTo("0");

		var attrResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"think hasattr(ClnCopy_{token}, FOO)"));
		await Assert.That(attrResult.Message!.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// @clone/preserve copies privileged flags too.
	/// PennMUSH testsidefx.t: clone.7-8
	/// </summary>
	[Test]
	public async ValueTask Clone_Preserve_CopiesPrivilegedFlags()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clp");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create ClpSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set ClpSrc_{token}=Wizard"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@clone/preserve ClpSrc_{token}=ClpCopy_{token}"));

		var flagResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"think hasflag(ClpCopy_{token}, WIZARD)"));
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

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create ClfSrc_{token}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set ClfSrc_{token}=Wizard"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"think clone(ClfSrc_{token}, ClfNP_{token})"));
		var npFlag = await Parser.CommandParse(1, ConnectionService, MModule.single($"think hasflag(ClfNP_{token}, WIZARD)"));
		await Assert.That(npFlag.Message!.ToPlainText()!.Trim()).IsEqualTo("0");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"think clone(ClfSrc_{token}, ClfP_{token}, , preserve)"));
		var pFlag = await Parser.CommandParse(1, ConnectionService, MModule.single($"think hasflag(ClfP_{token}, WIZARD)"));
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

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@clone NoSuchObj_{token}"));

		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"think clone(NoSuchObj_{token})"));
		var output = result.Message!.ToPlainText()!.Trim();
		// SharpMUSH returns "#-1 NO MATCH" for failed locate
		await Assert.That(output).StartsWith("#-1");
	}
}
