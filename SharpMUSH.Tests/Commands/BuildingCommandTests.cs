using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class BuildingCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(Actor.DbRef, Actor.Handle);
	private TestIsolationHelpers.TestPlayer Actor { get; set; } = null!;
	private readonly List<long> _handles = [];
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Before(Test)]
	public async Task SetUpActor()
	{
		Actor = await CreatePlayer("BuildingActor");
		var actor = (await Mediator.Send(new GetObjectNodeQuery(Actor.DbRef))).Expect<SharpPlayer>();
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(actor, wizard!))).IsTrue();
		var roomRef = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("BuildingRoom"), actor));
		var room = (await Mediator.Send(new GetObjectNodeQuery(roomRef))).Expect<SharpRoom>();
		var origin = await actor.Location.WithCancellation(CancellationToken.None);
		await Mediator.Send(new MoveObjectCommand(actor, room, origin.Object().DBRef, IsSilent: true));
	}

	[After(Test)]
	public async Task DisconnectActors()
	{
		foreach (var handle in _handles)
			await ConnectionService.Disconnect(handle);
	}

	private async Task<TestIsolationHelpers.TestPlayer> CreatePlayer(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		_handles.Add(player.Handle);
		return player;
	}

	private async Task<DBRef> CreateFixtureThing(string prefix)
	{
		var created = await TestIsolationHelpers.CreateObjectCommandAsync(
			Parser, ConnectionService, TestIsolationHelpers.GenerateUniqueName(prefix), Actor.Handle);
		return DBRef.Parse(created.Message.ToPlainText());
	}

	private async Task<SharpExit> ExitIn(DBRef room, string name) =>
		await Mediator.CreateStream(new GetExitsQuery(room)).SingleAsync(exit => exit.Object.Name == name);

	private async Task AssertDigNotifications(string roomName, DBRef source, DBRef room,
		SharpExit forward, SharpExit back)
	{
		var forwardDestination = (await forward.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();
		var backDestination = (await back.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();
		await Assert.That(forwardDestination.Object().DBRef).IsEqualTo(room);
		var sourceObject = (await Mediator.Send(new GetObjectNodeQuery(source))).Expect<AnySharpObject>();
		await Assert.That(backDestination.Object().DBRef).IsEqualTo(sourceObject.Object().DBRef);
		await NotifyService.Received(1).NotifyLocalized(Actor.DbRef,
			nameof(ErrorMessages.Notifications.RoomCreatedWithNumberFormat), TestHelpers.MatchingObject(Actor.DbRef),
			Arg.Is<object[]>(args => args.Length == 2 && args[0].ToString() == roomName && Equals(args[1], room.Number)));
		await NotifyService.Received(1).NotifyLocalized(Actor.DbRef,
			nameof(ErrorMessages.Notifications.LinkedExitToRoom), TestHelpers.MatchingObject(Actor.DbRef),
			Arg.Is<object[]>(args => args.Length == 2 && Equals(args[0], forward.Object.DBRef.Number) && Equals(args[1], room.Number)));
		await NotifyService.Received(2).NotifyLocalized(Actor.DbRef,
			nameof(ErrorMessages.Notifications.TryingToLink), TestHelpers.MatchingObject(Actor.DbRef),
			Arg.Is<object[]>(args => args.Length == 0));
		await NotifyService.Received(1).NotifyLocalized(Actor.DbRef,
			nameof(ErrorMessages.Notifications.LinkedExitToRoom), TestHelpers.MatchingObject(Actor.DbRef),
			Arg.Is<object[]>(args => args.Length == 2 && Equals(args[0], back.Object.DBRef.Number) && Equals(args[1], source.Number)));
	}

	[Test]
	public async ValueTask CreateObject()
	{
		var result = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create CreateObject - Test Object"));

		var newDb = DBRef.Parse(result.Message.ToPlainText());
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObject - Test Object");
	}

	[Test]
	public async ValueTask CreateObjectWithCost()
	{
		var result = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create CreateObjectWithCost - Test Object=10"));

		var newDb = DBRef.Parse(result.Message.ToPlainText());
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("CreateObjectWithCost - Test Object");
	}

	[Test]
	public async ValueTask DoDigForCommandListCheck()
	{
		var currentLocation = await Parser.FunctionParse(MarkupText.Plain("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message.ToPlainText());

		var newRoom = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain("@dig DoDigTestRoom=DoDigTestExit;DoDigTestExitAlias,DoDigTestExitBack;DoDigTestExitAliasBack"));

		var newDb = DBRef.Parse(newRoom.Message.ToPlainText());
		var forward = await ExitIn(currentLocationDbRef, "DoDigTestExit");
		var back = await ExitIn(newDb, "DoDigTestExitBack");

		await AssertDigNotifications("DoDigTestRoom", currentLocationDbRef, newDb, forward, back);
	}

	/// <summary>
	/// <c>fun_dig</c> is <c>do_dig</c> (<c>fundb.c:2177-2189</c>), so the function tells the digger the
	/// same things the command does (<c>create.c:504-512</c>).
	/// </summary>
	[Test]
	public async ValueTask DigFunctionNotifiesLikeTheCommand()
	{
		var currentLocation = await Parser.FunctionParse(MarkupText.Plain("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message.ToPlainText());

		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain("think dig(DigFnTestRoom,DigFnTestExit;DigFnTestExitAlias,DigFnTestExitBack;DigFnTestExitAliasBack)"));

		var forward = await ExitIn(currentLocationDbRef, "DigFnTestExit");
		var newDb = (await forward.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>().Object().DBRef;
		var back = await ExitIn(newDb, "DigFnTestExitBack");

		await AssertDigNotifications("DigFnTestRoom", currentLocationDbRef, newDb, forward, back);
	}

	[Test]
	public async ValueTask DoDigForCommandListCheck2()
	{
		var currentLocation = await Parser.FunctionParse(MarkupText.Plain("%l"));
		var currentLocationDbRef = DBRef.Parse(currentLocation!.Message.ToPlainText());

		var newRoom = await Parser.CommandListParse(MarkupText.Plain("@dig Foo Room={Exit;ExitAlias},{ExitBack;ExitAliasBack}"));

		var newDb = DBRef.Parse(newRoom!.Message.ToPlainText());
		var forward = await ExitIn(currentLocationDbRef, "Exit");
		var back = await ExitIn(newDb, "ExitBack");

		await AssertDigNotifications("Foo Room", currentLocationDbRef, newDb, forward, back);
	}


	[Test]
	public async Task DigAndMoveTest()
	{
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@dig NewRoom=Forward;F,Backward;B"));
		var initialRoom = (await Parser.FunctionParse(MarkupText.Plain("%l")))!.Message.ToPlainText();
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("goto Forward"));
		var newRoom = (await Parser.FunctionParse(MarkupText.Plain("%l")))!.Message.ToPlainText();
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("goto Backward"));
		var finalRoom = (await Parser.FunctionParse(MarkupText.Plain("%l")))!.Message.ToPlainText();

		await Assert.That(initialRoom).Length().IsPositive();
		await Assert.That(initialRoom).IsEqualTo(finalRoom);
		await Assert.That(newRoom).IsNotEqualTo(initialRoom);
	}

	[Test]
	public async ValueTask NameObject()
	{
		// Create an object first, capturing the dbref to avoid ambiguous name lookup
		var createResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create DigAndMoveTest - Rename Test"));
		var newDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);

		// Rename it using dbref to avoid "#-2 I DON'T KNOW WHICH ONE YOU MEAN" ambiguity
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@name {newDbRef}=DigAndMoveTest - New Name"));

		var renamedObject = await Mediator.Send(new GetObjectNodeQuery(newDbRef));
		await Assert.That(renamedObject.Object()!.Name).IsEqualTo("DigAndMoveTest - New Name");
	}

	[Test]
	public async ValueTask DigRoom()
	{
		var result = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@dig DigRoom - Test Room"));

		var newDb = DBRef.Parse(result.Message.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("DigRoom - Test Room");
	}

	[Test]
	public async ValueTask DigRoomWithExits()
	{
		var executor = Actor.DbRef;
		var result = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@dig Room With Exits=In;I,Out;O"));

		var newDb = DBRef.Parse(result.Message.ToPlainText()!);
		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));

		await Assert.That(newObject.Object()!.Name).IsEqualTo("Room With Exits");
		await NotifyService.Received(1).NotifyLocalized(executor,
			nameof(ErrorMessages.Notifications.RoomCreatedWithNumberFormat), TestHelpers.MatchingObject(executor),
			Arg.Is<object[]>(args => args.Length == 2 && args[0].ToString() == "Room With Exits" && Equals(args[1], newDb.Number)));
	}

	[Test]
	public async ValueTask LinkExit()
	{
		var executor = Actor.DbRef;
		var roomResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@dig LinkExitTestRoom"));
		var roomDbRef = DBRef.Parse(roomResult.Message.ToPlainText()!);

		var exitResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@open LinkExitTestExit"));
		var exitDbRef = DBRef.Parse(exitResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@link {exitDbRef}={roomDbRef}"));

		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitDbRef))).Expect<SharpExit>();
		var destination = (await exit.Home.WithCancellation(CancellationToken.None)).Expect<AnySharpContainer>();
		await Assert.That(destination.Object().DBRef).IsEqualTo(roomDbRef);
		// do_link names the destination through unparse_object (src/create.c:385-386), where
		// do_real_open prints a bare dbref.
		await WebAppFactoryArg.Notifications.WaitForAsync(executor,
			$"Linked exit #{exitDbRef.Number} to LinkExitTestRoom(#{roomDbRef.Number}R");
	}

	[Test]
	public async ValueTask CloneObject()
	{
		var executor = Actor.DbRef;
		var sourceResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create CloneObjectTestSource"));
		var sourceDbRef = DBRef.Parse(sourceResult.Message.ToPlainText()!);

		var cloneResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@clone {sourceDbRef}"));
		var cloneDbRef = DBRef.Parse(cloneResult.Message.ToPlainText());
		var clone = await Mediator.Send(new GetObjectNodeQuery(cloneDbRef));
		await Assert.That(clone.Object()!.Name).IsEqualTo("CloneObjectTestSource");
		await Assert.That(cloneDbRef).IsNotEqualTo(sourceDbRef);
		await NotifyService.Received(1).NotifyLocalized(executor,
			nameof(ErrorMessages.Notifications.ClonedObject), TestHelpers.MatchingObject(executor),
			Arg.Is<object[]>(args => args.Length == 1 && Equals(args[0], $"#{cloneDbRef.Number}")));
	}

	[Test]
	public async ValueTask ParentSetAndGet()
	{
		var parentResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create ParentTestObject"));
		var parentDbRef = DBRef.Parse(parentResult.Message.ToPlainText()!);

		var childResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create ChildTestObject"));
		var childDbRef = DBRef.Parse(childResult.Message.ToPlainText()!);

		var parentObj = await Mediator.Send(new GetObjectNodeQuery(parentDbRef));
		var childObj = (await Mediator.Send(new GetObjectNodeQuery(childDbRef))).Expect<AnySharpObject>();
		await Assert.That(parentObj.IsNone).IsFalse();

		var initialParent = await childObj.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(initialParent.IsNone).IsTrue();

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var updatedChild = (await Mediator.Send(new GetObjectNodeQuery(childDbRef))).Expect<AnySharpObject>();
		var setParent = (await updatedChild.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(setParent.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
	}

	[Test]
	public async ValueTask ParentUnset()
	{
		var parentResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create ParentUnsetTest_Parent"));
		var parentDbRef = DBRef.Parse(parentResult.Message.ToPlainText()!);

		var childResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create ParentUnsetTest_Child"));
		var childDbRef = DBRef.Parse(childResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var childWithParent = (await Mediator.Send(new GetObjectNodeQuery(childDbRef))).Expect<AnySharpObject>();
		var parentSet = await childWithParent.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentSet.IsNone).IsFalse();

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {childDbRef}=none"));

		var childNoParent = (await Mediator.Send(new GetObjectNodeQuery(childDbRef))).Expect<AnySharpObject>();
		var parentCleared = await childNoParent.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentCleared.IsNone).IsTrue();
	}

	[Test]
	public async ValueTask ParentCycleDetection_DirectCycle()
	{
		var executor = Actor.DbRef;
		var objAResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create CycleTest_A"));
		var objADbRef = DBRef.Parse(objAResult.Message.ToPlainText()!);

		var objBResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create CycleTest_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objADbRef}={objBDbRef}"));

		var objA = (await Mediator.Send(new GetObjectNodeQuery(objADbRef))).Expect<AnySharpObject>();
		var parentOfA = (await objA.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(parentOfA.Object().DBRef.Number).IsEqualTo(objBDbRef.Number);

		// Try to set B's parent to A (would create direct cycle: A -> B -> A)
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objBDbRef}={objADbRef}"));

		var objB = (await Mediator.Send(new GetObjectNodeQuery(objBDbRef))).Expect<AnySharpObject>();
		var parentOfB = await objB.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfB.IsNone).IsTrue();

		// Cycle through the chain (A -> B, then B -> A attempted) - Penn's "You are not allowed to
		// be your own ancestor!" (src/set.c:1477), not the self-reference message.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CyclicAncestor), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ParentCycleDetection_IndirectCycle()
	{
		var executor = Actor.DbRef;
		var objAResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create IndirectCycle_A"));
		var objADbRef = DBRef.Parse(objAResult.Message.ToPlainText()!);

		var objBResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create IndirectCycle_B"));
		var objBDbRef = DBRef.Parse(objBResult.Message.ToPlainText()!);

		var objCResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create IndirectCycle_C"));
		var objCDbRef = DBRef.Parse(objCResult.Message.ToPlainText()!);

		// Create chain: A -> B -> C
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objADbRef}={objBDbRef}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objBDbRef}={objCDbRef}"));

		var objA = (await Mediator.Send(new GetObjectNodeQuery(objADbRef))).Expect<AnySharpObject>();
		var parentOfA = (await objA.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(parentOfA.Object().DBRef.Number).IsEqualTo(objBDbRef.Number);

		var objB = (await Mediator.Send(new GetObjectNodeQuery(objBDbRef))).Expect<AnySharpObject>();
		var parentOfB = (await objB.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(parentOfB.Object().DBRef.Number).IsEqualTo(objCDbRef.Number);

		// Try to set C's parent to A (would create indirect cycle: A -> B -> C -> A)
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objCDbRef}={objADbRef}"));

		var objC = (await Mediator.Send(new GetObjectNodeQuery(objCDbRef))).Expect<AnySharpObject>();
		var parentOfC = await objC.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOfC.IsNone).IsTrue();

		// Indirect cycle (A -> B -> C, then C -> A attempted) - same Penn message as a direct cycle.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CyclicAncestor), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ParentCycleDetection_SelfParent()
	{
		var executor = Actor.DbRef;
		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create SelfParentTest"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		// Try to set object as its own parent (self-cycle)
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objDbRef}={objDbRef}"));

		var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var parent = await obj.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parent.IsNone).IsTrue();

		// Self-reference (@parent obj=obj) - Penn's "A thing cannot be its own ancestor!"
		// (src/set.c:1432), distinct from the cycle-through-chain message above.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SelfAncestor), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ParentCycleDetection_LongChain()
	{
		var executor = Actor.DbRef;
		var objDbRefs = new List<DBRef>();
		for (int i = 0; i < 5; i++)
		{
			var result = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create LongChain_{i}"));
			objDbRefs.Add(DBRef.Parse(result.Message.ToPlainText()!));
		}

		// Create chain: 0 -> 1 -> 2 -> 3 -> 4
		for (int i = 0; i < 4; i++)
		{
			await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objDbRefs[i]}={objDbRefs[i + 1]}"));
		}

		for (int i = 0; i < 4; i++)
		{
			var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRefs[i]))).Expect<AnySharpObject>();
			var parent = (await obj.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
			await Assert.That(parent.Object().DBRef.Number).IsEqualTo(objDbRefs[i + 1].Number);
		}

		// Try to set 4's parent to 0 (would create long cycle: 0 -> 1 -> 2 -> 3 -> 4 -> 0)
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {objDbRefs[4]}={objDbRefs[0]}"));

		var obj4 = (await Mediator.Send(new GetObjectNodeQuery(objDbRefs[4]))).Expect<AnySharpObject>();
		var parentOf4 = await obj4.Object().Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parentOf4.IsNone).IsTrue();

		// 5-object cycle wrapping back on itself - still a cycle-through-chain, not self-reference.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CyclicAncestor), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ChownObject()
	{
		var item = await CreateFixtureThing("ChownItem");
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "ChownOwner");
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chown {item}={owner}"));

		var changed = (await Mediator.Send(new GetObjectNodeQuery(item))).Expect<AnySharpObject>();
		var actualOwner = await changed.Object().Owner.WithCancellation(CancellationToken.None);
		await Assert.That(actualOwner.Object.DBRef).IsEqualTo(owner);
	}

	[Test]
	public async ValueTask ChzoneObject()
	{
		var executor = Actor.DbRef;
		var zoneResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create Zone Object"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message.ToPlainText()!);

		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create Zoned Object"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		// NotifyLocalized sends "Zone changed." via the ZoneChanged key
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();
	}

	/// <summary>
	/// <c>do_destroy</c> names its target with <c>unparse_object</c> (<c>pennmush/src/destroy.c:391</c>
	/// @ 80a1d5b), so the confirmation carries the dbref and flag letters and not just the name. The
	/// parity harness saw PennMUSH answer <c>RqA(#NEW1Tn) is scheduled to be destroyed.</c> where
	/// SharpMUSH answered <c>RqA is scheduled to be destroyed.</c>
	/// (<c>25-requested-dbref/dbref.holes</c> step 3).
	/// </summary>
	[Test]
	public async ValueTask RecycleObject()
	{
		var executor = Actor.DbRef;
		var name = TestIsolationHelpers.GenerateUniqueName("RecycleTest");
		var createResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create {name}"));
		var recycleDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);

		var before = WebAppFactoryArg.Notifications.CountFor(executor);
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@recycle {recycleDbRef}"));

		var recycled = (await Mediator.Send(new GetObjectNodeQuery(recycleDbRef))).Expect<AnySharpObject>();
		await Assert.That(await recycled.Object().Flags.Value.AnyAsync(flag => flag.Name == "GOING")).IsTrue();

		// The flag letters themselves are whatever the object carries; what parity turns on is that the
		// name is followed by "(#<dbref><flags>)" rather than standing alone.
		var scheduled = WebAppFactoryArg.Notifications.For(executor).Skip(before)
			.First(message => message.EndsWith(" is scheduled to be destroyed.", StringComparison.Ordinal));

		await Assert.That(scheduled).StartsWith($"{name}(#{recycleDbRef.Number}");
		await Assert.That(scheduled).EndsWith(") is scheduled to be destroyed.");
	}

	/// <summary>
	/// <c>free_object()</c> reaches <c>do_halt()</c>, which tells the object's owner
	/// <c>Halted: &lt;name&gt;(#&lt;dbref&gt;)</c> (<c>pennmush/src/cque.c:2176-2178</c> @ 80a1d5b) —
	/// the bare dbref, because <c>do_halt</c> formats it itself instead of calling
	/// <c>unparse_object</c>. SharpMUSH stopped the queue silently.
	/// </summary>
	[Test]
	public async ValueTask RecycleTwiceReportsTheHaltToTheOwner()
	{
		var executor = Actor.DbRef;
		var name = TestIsolationHelpers.GenerateUniqueName("HaltReport");
		var createResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create {name}"));
		var doomed = DBRef.Parse(createResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@recycle {doomed}"));

		var before = WebAppFactoryArg.Notifications.CountFor(executor);
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@recycle {doomed}"));

		await Assert.That(WebAppFactoryArg.Notifications.For(executor).Skip(before))
			.Contains($"Halted: {name}(#{doomed.Number})");
	}

	/// <summary>
	/// <c>do_halt</c> gates that report on <c>!Quiet(Owner(player))</c>
	/// (<c>pennmush/src/cque.c:2176</c> @ 80a1d5b), so a QUIET owner is told nothing.
	/// </summary>
	[Test]
	public async ValueTask RecycleTwiceTellsAQuietOwnerNothing()
	{
		var executor = Actor.DbRef;
		var name = TestIsolationHelpers.GenerateUniqueName("QuietHalt");
		var createResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create {name}"));
		var doomed = DBRef.Parse(createResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@recycle {doomed}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@set me=QUIET"));

		var before = WebAppFactoryArg.Notifications.CountFor(executor);
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@recycle {doomed}"));

		await Assert.That(WebAppFactoryArg.Notifications.For(executor).Skip(before))
			.DoesNotContain($"Halted: {name}(#{doomed.Number})");
	}

	[Test]
	public async ValueTask UnlinkExit()
	{
		var executor = Actor.DbRef;

		var roomName = TestIsolationHelpers.GenerateUniqueName("UnlinkRoom");
		var digResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomDbRef = digResult.Message.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("UnlinkExit");
		var openResult = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={roomDbRef}"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@unlink {exitName}"));

		DBRef.TryParse(openResult.Message.ToPlainText()!.Trim(), out var exitRef);
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitRef!.Value))).Expect<SharpExit>();
		var destination = await exit.Home.WithCancellation(CancellationToken.None);

		await Assert.That(destination.IsNone).IsTrue();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.UnlinkedExit), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask SetFlag()
	{
		// Create a unique thing to set the flag on, instead of modifying shared God (#1).
		var thingDbRef = await CreateFixtureThing("SetFlagTest");
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {thingDbRef}=MONITOR"));

		var thing = (await Mediator.Send(new GetObjectNodeQuery(thingDbRef))).Expect<SharpThing>();
		var flags = await thing.Object.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Any(x => x.Name == "MONITOR")).IsTrue();
	}

	[Test]
	public async ValueTask LockObject()
	{
		var executor = Actor.DbRef;
		// Create a unique object for this test to avoid pollution
		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create LockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@lock {objDbRef}=#TRUE"));

		var locked = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		await Assert.That(locked.Object().Locks["Basic"].LockString).IsEqualTo("#TRUE");

		await NotifyService.Received(1).NotifyLocalized(executor,
			nameof(ErrorMessages.Notifications.ObjectLocked), TestHelpers.MatchingObject(executor),
			Arg.Is<object[]>(args => args.Length == 3 && Equals(args[0], "LockObjectTest") &&
				Equals(args[1], objDbRef.Number) && Equals(args[2], "Basic")));
	}

	[Test]
	public async ValueTask UnlockObject()
	{
		var executor = Actor.DbRef;
		// Create a unique object for this test to avoid pollution
		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create UnlockObjectTest"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@lock {objDbRef}=#TRUE"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@unlock {objDbRef}"));

		var unlocked = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		await Assert.That(unlocked.Object().Locks.ContainsKey("Basic")).IsFalse();

		await NotifyService.Received(1).NotifyLocalized(executor,
			nameof(ErrorMessages.Notifications.ObjectUnlocked), TestHelpers.MatchingObject(executor),
			Arg.Is<object[]>(args => args.Length == 3 && Equals(args[0], "UnlockObjectTest") &&
				Equals(args[1], objDbRef.Number) && Equals(args[2], "Basic")));
	}

	/// <summary>
	/// Tests that @desc (and other @attribute commands) store their argument without evaluating it.
	/// PennMUSH defers evaluation until the attribute is used.
	/// Using @desc to verify prefix matching correctly chooses DESCRIBE over DESCFORMAT (shorter match wins).
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_StoresContentsWithoutEvaluation()
	{
		var executor = Actor.DbRef;
		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create DescEvalTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		// @desc should match DESCRIBE (not DESCFORMAT) due to length sorting in prefix matching
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=[add(47119,82)]"));

		await NotifyService.Received(1).NotifyLocalized(Actor.Handle,
			nameof(ErrorMessages.Notifications.AttributeSet), TestHelpers.MatchingObject(executor),
			Arg.Is<object[]>(args => args.Length == 2 && Equals(args[0], "DescEvalTestObject") && Equals(args[1], "DESCRIBE")));

		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var descAttr = (await attributeService.GetAttributeAsync(
			obj, obj, "DESCRIBE",
			IAttributeService.AttributeMode.Read, false)).Expect<SharpAttribute[]>();

		var storedValue = descAttr.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("[add(47119,82)]");
	}

	/// <summary>
	/// Tests that @desc (prefix) works and correctly matches DESCRIBE over DESCFORMAT.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_PrefixMatch_Works()
	{
		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create DescPrefixMatchTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		// Use @desc (prefix) - should match DESCRIBE, not DESCFORMAT, due to shorter name
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=Test description text"));

		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var descAttr = (await attributeService.GetAttributeAsync(
			obj, obj, "DESCRIBE",
			IAttributeService.AttributeMode.Read, false)).Expect<SharpAttribute[]>();

		var storedValue = descAttr.Last().Value.ToPlainText();
		await Assert.That(storedValue).IsEqualTo("Test description text");
	}

	/// <summary>
	/// Tests that look displays a literal DESCRIBE value.
	/// </summary>
	[Test]
	public async ValueTask Look_DisplaysLiteralDescribe()
	{
		var executor = Actor.DbRef;
		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create LookDescTestObject"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		// Use @desc with a unique literal value to verify look displays it unchanged.
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=LookDesc_UniqueTestValue_38471"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"look {objDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
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
		var player = await CreatePlayer($"LookRoomDF{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomDF_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@desc here=roomdesc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&DESCFORMAT here=[ucstr(%0)]"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"ROOMDESC_{token.ToUpper()}")), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Looking at a room evaluates @describe, then passes the evaluated text to @descformat as %0.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_EvaluatesDescribeBeforePassingItToDescFormat()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lde");
		var player = await CreatePlayer($"LookRoomEval{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomEval_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));

		var room = (await Mediator.Send(new GetObjectNodeQuery(roomDbRef))).Expect<AnySharpObject>();
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		await attributeService.SetAttributeAsync(playerObject, room, "DESCRIBE", MarkupText.Plain("[add(47119,82)]"));
		await attributeService.SetAttributeAsync(playerObject, room, "DESCFORMAT", MarkupText.Plain($"evaluated_{token}:%0"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"evaluated_{token}:47201")), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Looking at a room queues its @adescribe action with the looker as enactor.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_TriggersAdescribe()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lad");
		var player = await CreatePlayer($"LookRoomAdesc{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomAdesc_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@adesc here=@pemit %#=adesc_{token}_from_%!"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var expectedMessage = $"adesc_{token}_from_#{roomDbRef.Number}";
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, expectedMessage);
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, expectedMessage)), TestHelpers.MatchingObject(roomDbRef), INotifyService.NotificationType.PrivateEmit);
	}

	/// <summary>
	/// A halted room cannot run its @adescribe action when viewed.
	/// </summary>
	[Test]
	public async ValueTask Look_HaltedRoom_DoesNotTriggerAdescribe()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lhra");
		var player = await CreatePlayer($"LookHaltedRoom{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@dig HaltedRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message.ToPlainText()!.Trim());
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
		var parentResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@dig LookParent_{token}"));
		var parentDbRef = DBRef.Parse(parentResult.Message.ToPlainText()!.Trim());
		var childResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@dig LookChild_{token}"));
		var childDbRef = DBRef.Parse(childResult.Message.ToPlainText()!.Trim());
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@desc {parentDbRef}=inherited_[add(20,22)]"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&DESCFORMAT {parentDbRef}=parent_{token}:%0"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/DESCRIBE=!visual"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/DESCFORMAT=!visual"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var player = await CreatePlayer($"LookInherit{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={childDbRef}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, $"parent_{token}:inherited_42")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// The inside-description path applies the same parent and permission rules as @describe.
	/// </summary>
	[Test]
	public async ValueTask Look_InsideThing_UsesInheritedPrivateIdescribeAndFormatForNonOwner()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("liip");
		var parentResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create InsideParent_{token}"));
		var parentDbRef = DBRef.Parse(parentResult.Message.ToPlainText()!.Trim());
		var childResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create InsideChild_{token}"));
		var childDbRef = DBRef.Parse(childResult.Message.ToPlainText()!.Trim());
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&IDESCRIBE {parentDbRef}=inside_[add(20,22)]"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&IDESCFORMAT {parentDbRef}=inner_{token}:%0"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/IDESCRIBE=!visual"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {parentDbRef}/IDESCFORMAT=!visual"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var player = await CreatePlayer($"LookInside{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={childDbRef}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, $"inner_{token}:inside_42")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A forced look evaluates descriptions and actions with the forced player, not the forcer, as enactor.
	/// </summary>
	[Test]
	public async ValueTask Look_ForcedPlayer_IsEnactorForDescriptionFormatAndAction()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lfi");
		var roomResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@dig ForceRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message.ToPlainText()!.Trim());
		var player = await CreatePlayer($"ForceLook{token}");
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@tel {player.DbRef}={roomDbRef}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@desc {roomDbRef}=desc:%#:%@"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&DESCFORMAT {roomDbRef}=format_{token}:%#:%@:%0"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {roomDbRef}/DESCFORMAT=visual"));
		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@adesc {roomDbRef}=@pemit %#=action_{token}:%#:%@:%!"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@force {player.DbRef}=look"));

		var playerRef = $"#{player.DbRef.Number}";
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef,
			$"action_{token}:{playerRef}:{playerRef}:#{roomDbRef.Number}");
		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg,
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
		var player = await CreatePlayer($"LookEmpty{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig EmptyRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@desc here="));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&DESCFORMAT here=empty_{token}:%+:%0:end"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, $"empty_{token}:1::end")),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A present but empty @descformat suppresses an otherwise non-empty description.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_EmptyDescFormatSuppressesDescription()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lefs");
		var player = await CreatePlayer($"LookEmptyFormat{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig EmptyFormatRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message.ToPlainText()!.Trim());
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
		var player = await CreatePlayer($"LookSnapshot{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var roomResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig SnapshotRoom_{token}"));
		var roomDbRef = DBRef.Parse(roomResult.Message.ToPlainText()!.Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={roomDbRef}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@adesc here=@pemit %#=snapshot_old_{token}"));

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@dolist 1={{look; @adesc here=@pemit %#=snapshot_new_{token}}}"));

		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, $"snapshot_old_{token}");
		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(player.DbRef),
			Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, $"snapshot_old_{token}")),
			TestHelpers.MatchingObject(roomDbRef), INotifyService.NotificationType.PrivateEmit);
	}

	/// <summary>
	/// Looking at a room with no @describe still evaluates @descformat, without supplying %0.
	/// </summary>
	[Test]
	public async ValueTask Look_Room_NoDescription_UsesDescFormatWithoutArgument()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lnd");
		var player = await CreatePlayer($"LookRoomND{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomND_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message.ToPlainText()!.Trim());
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
		var player = await CreatePlayer($"LookRoomDefault{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var digResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig RoomDefault_{token}"));
		var roomDbRef = DBRef.Parse(digResult.Message.ToPlainText()!.Trim());
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
		var player = await CreatePlayer($"LookThing{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var objResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create ThingIT_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!.Trim());
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
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<SharpMessage>(msg =>
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
		var player = await CreatePlayer($"LookIdescFallback{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var objResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create ThingIF_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!.Trim());
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
		var player = await CreatePlayer($"LookIdesc{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var objResult = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create ThingII_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!.Trim());
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
			.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"INSIDEDESC_{token.ToUpper()}")), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// A halted container cannot run its @aidescribe action when viewed from inside.
	/// </summary>
	[Test]
	public async ValueTask Look_HaltedContainer_DoesNotTriggerAidescribe()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("lhca");
		var player = await CreatePlayer($"LookHaltedContainer{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);
		var objResult = await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@create HaltedContainer_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!.Trim());
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
		var player = await CreatePlayer($"LookIdescNoFormat{token}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var objResult = await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@create ThingIINF_{token}"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!.Trim());
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
		var testPlayer = await CreatePlayer("DescribeCommand");
		var testParser = WebAppFactoryArg.CommandParserFor(testPlayer.DbRef, testPlayer.Handle);

		await testParser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@desc #99999=test description"));

		// do_set's failed match is match.c:485's "I can't see that here."; "I don't see that here." is
		// look.c/move.c's string, for the commands that match for themselves.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "I can't see that here.")), TestHelpers.MatchingObject(testPlayer.DbRef),
				INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Tests that @desc without = clears the attribute.
	/// </summary>
	[Test]
	public async ValueTask DescribeCommand_MissingEquals_ClearsAttribute()
	{
		var executor = Actor.DbRef;
		var objResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@create DescClearTest"));
		var objDbRef = DBRef.Parse(objResult.Message.ToPlainText()!);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}=Initial description"));

		var initial = await Mediator.CreateStream(new GetAttributeQuery(objDbRef, ["DESCRIBE"])).SingleAsync();
		await Assert.That(initial.Value.ToPlainText()).IsEqualTo("Initial description");

		// Now clear it by using @desc without =
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@desc {objDbRef}"));

		await Assert.That(await Mediator.CreateStream(new GetAttributeQuery(objDbRef, ["DESCRIBE"])).AnyAsync()).IsFalse();

		await NotifyService.Received(1).NotifyLocalized(Actor.Handle,
			nameof(ErrorMessages.Notifications.AttributeCleared), TestHelpers.MatchingObject(executor),
			Arg.Is<object[]>(args => args.Length == 2 && Equals(args[0], "DescClearTest") && Equals(args[1], "DESCRIBE")));
	}

	/// <summary>
	/// @clone copies attributes but strips privileged flags.
	/// PennMUSH testsidefx.t: clone.1-6
	/// </summary>
	[Test]
	public async ValueTask Clone_CopiesAttrs_StripsPrivilegedFlags()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("cln");

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create ClnSrc_{token}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set ClnSrc_{token}=Wizard"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&FOO ClnSrc_{token}=blah_{token}"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@clone ClnSrc_{token}=ClnCopy_{token}"));

		var flagResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think hasflag(ClnCopy_{token}, WIZARD)"));
		await Assert.That(flagResult.Message.ToPlainText()!.Trim()).IsEqualTo("0");

		var attrResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think hasattr(ClnCopy_{token}, FOO)"));
		await Assert.That(attrResult.Message.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// The configured creation defaults reach the object. The defaults are looked up through the flag
	/// store directly, in the case the config supplies them.
	/// </summary>
	[Test]
	public async ValueTask Create_AppliesTheConfiguredDefaultFlags()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("dflt");

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create DfltThing_{token}"));

		var flagged = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"think hasflag(DfltThing_{token}, NO_COMMAND)"));

		await Assert.That(flagged.Message.ToPlainText()!.Trim()).IsEqualTo("1")
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

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create ClncSrc_{token}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set ClncSrc_{token}=!NO_COMMAND"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@clone ClncSrc_{token}=ClncCopy_{token}"));

		var cloned = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"think hasflag(ClncCopy_{token}, NO_COMMAND)"));

		await Assert.That(cloned.Message.ToPlainText()!.Trim()).IsEqualTo("0")
			.Because("the clone's flags are synchronised to the source, not unioned with the defaults");
	}

	/// <summary>A flag the source does have still reaches the clone.</summary>
	[Test]
	public async ValueTask Clone_KeepsANonDefaultFlagTheSourceHas()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clnk");

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create ClnkSrc_{token}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set ClnkSrc_{token}=OPAQUE"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@clone ClnkSrc_{token}=ClnkCopy_{token}"));

		var cloned = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"think hasflag(ClnkCopy_{token}, OPAQUE)"));

		await Assert.That(cloned.Message.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// @clone/preserve copies privileged flags too.
	/// PennMUSH testsidefx.t: clone.7-8
	/// </summary>
	[Test]
	public async ValueTask Clone_Preserve_CopiesPrivilegedFlags()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clp");

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create ClpSrc_{token}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set ClpSrc_{token}=Wizard"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@clone/preserve ClpSrc_{token}=ClpCopy_{token}"));

		var flagResult = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think hasflag(ClpCopy_{token}, WIZARD)"));
		await Assert.That(flagResult.Message.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// clone() function works like @clone; clone(..., preserve) like @clone/preserve.
	/// PennMUSH testsidefx.t: clone.9-12
	/// </summary>
	[Test]
	public async ValueTask CloneFunction_WithAndWithoutPreserve()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clf");

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@create ClfSrc_{token}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set ClfSrc_{token}=Wizard"));

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think clone(ClfSrc_{token}, ClfNP_{token})"));
		var npFlag = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think hasflag(ClfNP_{token}, WIZARD)"));
		await Assert.That(npFlag.Message.ToPlainText()!.Trim()).IsEqualTo("0");

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think clone(ClfSrc_{token}, ClfP_{token}, , preserve)"));
		var pFlag = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think hasflag(ClfP_{token}, WIZARD)"));
		await Assert.That(pFlag.Message.ToPlainText()!.Trim()).IsEqualTo("1");
	}

	/// <summary>
	/// @clone and clone() of non-existent object returns error.
	/// PennMUSH testsidefx.t: clone.13-14
	/// </summary>
	[Test]
	public async ValueTask Clone_NonExistentObject_Errors()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("clx");

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@clone NoSuchObj_{token}"));

		var result = await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"think clone(NoSuchObj_{token})"));
		var output = result.Message.ToPlainText()!.Trim();
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
		return BareDbref(home!.Message.ToPlainText().Trim());
	}

	/// <summary>
	/// <c>do_link</c> gates the home destination as well as the object (<c>src/create.c:404</c>):
	/// <c>!controls(player, room) &amp;&amp; !Abode(room)</c>. Any non-exit can be a home, so without
	/// this gate controlling the object alone would be enough to park it in a stranger's inventory.
	/// </summary>
	[Test]
	public async ValueTask LinkingAHomeNeedsControlOfTheDestinationOrAbode()
	{
		var linker = await CreatePlayer("LinkGateOwner");
		var stranger = await CreatePlayer("LinkGateStranger");

		var item = await CreateFixtureThing("LinkGateItem");
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chown #{item.Number}=#{linker.DbRef.Number}"));

		var abode = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("LinkGateAbode")}"));
		var abodeRoom = BareDbref(abode.Message.ToPlainText().Trim());
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {abodeRoom}=ABODE"));

		var before = await HomeOf(item);

		// Neither controlled nor ABODE: refused, and the home is untouched.
		var recorder = WebAppFactoryArg.Notifications;
		var seen = recorder.CountFor(linker.DbRef);
		var refusal = await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"@link #{item.Number}=#{stranger.DbRef.Number}"));
		await Assert.That(refusal.Message.ToPlainText().Trim()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);

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
		var linker = await CreatePlayer("LinkFnOwner");
		var stranger = await CreatePlayer("LinkFnStranger");

		var item = await CreateFixtureThing("LinkFnItem");
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chown #{item.Number}=#{linker.DbRef.Number}"));

		var before = await HomeOf(item);

		var refused = await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{item.Number}, #{stranger.DbRef.Number})"));
		await Assert.That(refused.Message.ToPlainText().Trim()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await HomeOf(item)).IsEqualTo(before);

		var allowed = await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{item.Number}, #{linker.DbRef.Number})"));
		await Assert.That(allowed.Message.ToPlainText().Trim()).IsEqualTo("1");
		await Assert.That(await HomeOf(item)).IsEqualTo($"#{linker.DbRef.Number}");
	}

	private async Task<string> OwnerOf(DBRef reference)
	{
		var owner = await Parser.FunctionParse(MarkupText.Plain($"[owner(#{reference.Number})]"));
		return BareDbref(owner!.Message.ToPlainText().Trim());
	}

	private async Task<DBRef> UnlinkedExit(string prefix)
	{
		var opened = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@open {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		return DBRef.Parse(opened.Message.ToPlainText()!);
	}

	/// <summary>A room anyone may link into: <c>can_link_to</c>'s <c>LINK_OK</c> half.</summary>
	private async Task<DBRef> LinkableRoom(string prefix)
	{
		var dug = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		var room = DBRef.Parse(dug.Message.ToPlainText()!);
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {room}=LINK_OK"));
		return room;
	}

	private async Task<DBRef?> DestinationOf(DBRef exitReference)
	{
		var exit = (await Mediator.Send(new GetObjectNodeQuery(exitReference))).Expect<SharpExit>();
		return await exit.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
			? destination.Object().DBRef
			: null;
	}

	/// <summary>
	/// <c>fun_link</c> is one call to <c>do_link</c> (<c>src/fundb.c:2219-2237</c>), whose exit
	/// destination is anything <c>can_link_to</c> admits — an exit may lead to a player or a thing, not
	/// only a room. The second copy of the function refused everything but a room.
	/// </summary>
	[Test]
	public async ValueTask LinkFunctionLinksAnExitToAnyContainer()
	{
		var exitDbRef = await UnlinkedExit("LinkFnAnyExit");
		var thing = await CreateFixtureThing("LinkFnAnyDestination");

		var linked = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{exitDbRef.Number}, #{thing.Number})"));

		await Assert.That(linked.Message.ToPlainText().Trim()).IsEqualTo("1");
		await Assert.That(await DestinationOf(exitDbRef)).IsEqualTo(thing);
	}

	/// <summary>
	/// <c>do_link</c>'s exit gate is <c>controls(player, thing) || (Location(thing) == NOTHING &amp;&amp;
	/// eval_lock(Link_Lock))</c> (<c>src/create.c:342-348</c>): an exit that leads nowhere may be linked
	/// — and by that act seized, <c>chown_object</c> at <c>:371</c> — by anyone who passes its
	/// <c>@lock/link</c>. SharpMUSH asked for control up front, so the lock half and the transfer
	/// behind it were both unreachable.
	/// </summary>
	[Test]
	public async ValueTask LinkingAnUnlinkedExitThroughItsLinkLockSeizesIt()
	{
		var owner = await CreatePlayer("LinkLockOwner");
		var linker = await CreatePlayer("LinkLockLinker");

		var exitDbRef = await UnlinkedExit("LinkLockExit");
		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@chown {exitDbRef}=#{owner.DbRef.Number}"));
		var destination = await LinkableRoom("LinkLockRoom");

		// A link lock nobody passes leaves the exit alone.
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@lock/link {exitDbRef}=#FALSE"));
		await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"@link {exitDbRef}={destination}"));

		await Assert.That(await DestinationOf(exitDbRef)).IsNull();
		await Assert.That(await OwnerOf(exitDbRef)).IsEqualTo($"#{owner.DbRef.Number}");

		// One it passes links the exit and hands it over.
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@lock/link {exitDbRef}=#TRUE"));
		await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"@link {exitDbRef}={destination}"));

		await Assert.That(await DestinationOf(exitDbRef)).IsEqualTo(destination);
		await Assert.That(await OwnerOf(exitDbRef)).IsEqualTo($"#{linker.DbRef.Number}");
	}

	/// <summary>
	/// <c>do_link</c> refuses <c>preserve</c> to anyone but a wizard (<c>src/create.c:352-355</c>), and
	/// keeps the owner when a wizard asks for it (<c>:369</c>, <c>:375-378</c>). <c>link()</c> reads it
	/// from <c>parse_boolean(args[2])</c> (<c>src/fundb.c:2232-2233</c>). SharpMUSH declared both the
	/// third argument and the <c>/PRESERVE</c> switch and read neither.
	/// </summary>
	[Test]
	public async ValueTask LinkPreserveIsWizardOnlyAndKeepsTheOwner()
	{
		var owner = await CreatePlayer("LinkPreserveOwner");
		var linker = await CreatePlayer("LinkPreserveLinker");

		var exitDbRef = await UnlinkedExit("LinkPreserveExit");
		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@chown {exitDbRef}=#{owner.DbRef.Number}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@lock/link {exitDbRef}=#TRUE"));
		var destination = await LinkableRoom("LinkPreserveRoom");

		var refused = await Parser.CommandParse(linker.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{exitDbRef.Number}, #{destination.Number}, 1)"));

		await Assert.That(refused.Message.ToPlainText().Trim()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await DestinationOf(exitDbRef)).IsNull();
		await Assert.That(await OwnerOf(exitDbRef)).IsEqualTo($"#{owner.DbRef.Number}");

		// A wizard's /preserve links the exit without taking it.
		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@link/preserve {exitDbRef}={destination}"));

		await Assert.That(await DestinationOf(exitDbRef)).IsEqualTo(destination);
		await Assert.That(await OwnerOf(exitDbRef)).IsEqualTo($"#{owner.DbRef.Number}");
	}

	/// <summary>
	/// <c>do_link</c> with no destination is <c>do_unlink</c> (<c>src/create.c:321-324</c>) — checked
	/// before it matches the object at all — and it returns 0 whether or not the unlink went through.
	/// SharpMUSH's <c>@LINK</c> declared <c>MinArgs = 2</c>, so the fallback was unreachable and
	/// <c>@link foo=</c> answered "expects at least 2 arguments".
	/// </summary>
	[Test]
	public async ValueTask LinkWithNoDestinationUnlinksTheExit()
	{
		var exitDbRef = await UnlinkedExit("LinkUnlinkFallbackExit");
		var destination = await LinkableRoom("LinkUnlinkFallbackRoom");

		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@link {exitDbRef}={destination}"));
		await Assert.That(await DestinationOf(exitDbRef)).IsEqualTo(destination);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@link {exitDbRef}="));

		await Assert.That(await DestinationOf(exitDbRef)).IsNull();
	}

	/// <summary>
	/// <c>fun_link</c> is <c>safe_integer(do_link(...))</c> (<c>src/fundb.c:2236</c>), so
	/// <c>link(&lt;exit&gt;,)</c> reaches the same <c>do_unlink</c> fallback and still reports failure.
	/// SharpMUSH's copy tried to match the empty string as a destination instead.
	/// </summary>
	[Test]
	public async ValueTask LinkFunctionWithNoDestinationUnlinksTheExit()
	{
		var exitDbRef = await UnlinkedExit("LinkFnUnlinkFallbackExit");
		var destination = await LinkableRoom("LinkFnUnlinkFallbackRoom");

		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@link {exitDbRef}={destination}"));
		await Assert.That(await DestinationOf(exitDbRef)).IsEqualTo(destination);

		var unlinked = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{exitDbRef.Number},)"));

		await Assert.That(unlinked.Message.ToPlainText().Trim())
			.IsEqualTo(ErrorMessages.Returns.MissingArguments);
		await Assert.That(await DestinationOf(exitDbRef)).IsNull();
	}

	/// <summary>
	/// <c>do_unlink</c> adds <c>MAT_CONTROL</c> to its match for anyone who is not a wizard
	/// (<c>src/create.c:256-258</c>), so an exit someone else owns never resolves and the fallback
	/// cannot be used to strip it. The match is silent, which is why the answer is "Unlink what?" and
	/// not the <c>controls()</c> refusal below it — that arm is for a wizard, who controls everything
	/// the match could have handed back.
	/// </summary>
	[Test]
	public async ValueTask LinkWithNoDestinationStillNeedsControlOfTheExit()
	{
		var owner = await CreatePlayer("UnlinkFallbackOwner");
		var stranger = await CreatePlayer("UnlinkFallbackStranger");

		var exitDbRef = await UnlinkedExit("UnlinkFallbackExit");
		var destination = await LinkableRoom("UnlinkFallbackRoom");
		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@link {exitDbRef}={destination}"));
		await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@chown {exitDbRef}=#{owner.DbRef.Number}"));

		var refused = await Parser.CommandParse(stranger.Handle, ConnectionService,
			MarkupText.Plain($"think link(#{exitDbRef.Number},)"));

		await Assert.That(refused.Message.ToPlainText().Trim())
			.IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(await DestinationOf(exitDbRef)).IsEqualTo(destination);

		// Pattern C: `stranger` is unique to this test, so the receiver pins the call.
		await NotifyService
			.Received(1)
			.NotifyAndReturn(stranger.DbRef, ErrorMessages.Returns.NoMatch,
				ErrorMessages.Notifications.UnlinkWhat, Arg.Any<bool>());
	}

	/// <summary>
	/// <c>do_unlink</c> matches <c>MAT_EXIT | MAT_HERE | MAT_ABSOLUTE</c> (<c>src/create.c:254</c>) —
	/// no MAT_NEIGHBOR, no MAT_POSSESSION, no MAT_PLAYER. A thing standing in the room is not something
	/// <c>@unlink</c> can name, and because the match is silent the answer is "Unlink what?" rather than
	/// the locator's "I can't see that here." Matching with <see cref="LocateFlags.All"/> resolved it
	/// and then refused it as the wrong type.
	/// </summary>
	[Test]
	public async ValueTask UnlinkWillNotMatchANeighbourByName()
	{
		var thingName = TestIsolationHelpers.GenerateUniqueName("UnlinkNeighbour");
		await TestIsolationHelpers.CreateObjectCommandAsync(Parser, ConnectionService, thingName, Actor.Handle);

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@unlink {thingName}"));

		// Pattern C: Actor is created fresh per test, so the receiver pins the call.
		await NotifyService
			.Received(1)
			.NotifyAndReturn(Actor.DbRef, ErrorMessages.Returns.NoMatch,
				ErrorMessages.Notifications.UnlinkWhat, Arg.Any<bool>());
	}

	/// <summary>
	/// <c>@unlink here</c>: <c>MAT_HERE</c> is in <c>do_unlink</c>'s flags, and the <c>TYPE_EXIT</c>
	/// preference is not <c>MAT_TYPE</c>, so a room still matches and its drop-to comes off
	/// (<c>src/create.c:278-282</c>).
	/// </summary>
	[Test]
	public async ValueTask UnlinkHereRemovesTheRoomsDropTo()
	{
		var destination = await LinkableRoom("UnlinkDropToTarget");
		var here = (await (await Mediator.Send(new GetObjectNodeQuery(Actor.DbRef))).Expect<AnySharpObject>().Where())
			.Object().DBRef;

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@link {here}={destination}"));
		var room = (await Mediator.Send(new GetObjectNodeQuery(here))).Expect<SharpRoom>();
		await Assert.That((await room.Location.WithCancellation(CancellationToken.None)).IsNone).IsFalse();

		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain("@unlink here"));

		var after = (await Mediator.Send(new GetObjectNodeQuery(here))).Expect<SharpRoom>();
		await Assert.That((await after.Location.WithCancellation(CancellationToken.None)).IsNone).IsTrue();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.DropToRemoved), Actor.DbRef, Actor.DbRef)).IsTrue();
	}

	/// <summary>
	/// Everything <see cref="Actor"/> was told while <paramref name="action"/> ran, read from the
	/// recipient-keyed recorder rather than the session-shared substitute's call list.
	/// </summary>
	private async Task<List<string>> ActorHeardWhile(Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(Actor.DbRef);
		await action();
		return [.. recorder.For(Actor.DbRef).Skip(before)];
	}

	/// <summary>
	/// <c>do_create</c> reports the bare dbref and nothing else:
	/// <c>notify_format(player, T("Created: Object %s."), unparse_dbref(thing))</c>
	/// (<c>src/create.c:604</c>). SharpMUSH said <c>Created &lt;name&gt; (#N:&lt;ctime&gt;).</c>, which
	/// leaks the creation stamp into a line imported softcode parses.
	/// </summary>
	[Test]
	public async ValueTask CreateReportsTheBareDbrefPennMUSHPrints()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("CreatedLine");
		DBRef? created = null;

		var heard = await ActorHeardWhile(async () =>
		{
			var result = await Parser.CommandParse(Actor.Handle, ConnectionService,
				MarkupText.Plain($"@create {name}"));
			created = DBRef.Parse(result.Message.ToPlainText());
		});

		await Assert.That(heard).Contains($"Created: Object #{created!.Value.Number}.");
		await Assert.That(heard.Any(line => line.Contains(name, StringComparison.Ordinal))).IsFalse()
			.Because("do_create names neither the object nor its creation time");
	}

	/// <summary>
	/// <c>do_clone</c>'s thing branch (<c>src/create.c:727</c>) is the same bare dbref under a different
	/// verb. SharpMUSH said <c>Cloned. New object: #N.</c>
	/// </summary>
	[Test]
	public async ValueTask CloneOfAThingReportsClonedObject()
	{
		var source = await CreateFixtureThing("ClonedLine");
		DBRef? clone = null;

		var heard = await ActorHeardWhile(async () =>
		{
			var result = await Parser.CommandParse(Actor.Handle, ConnectionService,
				MarkupText.Plain($"@clone {source}"));
			clone = DBRef.Parse(result.Message.ToPlainText());
		});

		await Assert.That(heard).Contains($"Cloned: Object #{clone!.Value.Number}.");
	}

	/// <summary>
	/// A cloned room says <c>Cloned: Room #%d.</c> instead (<c>src/create.c:744</c>) — one verb per type,
	/// which the single format string could not express.
	/// </summary>
	[Test]
	public async ValueTask CloneOfARoomReportsClonedRoom()
	{
		var source = await LinkableRoom("ClonedRoomLine");
		DBRef? clone = null;

		var heard = await ActorHeardWhile(async () =>
		{
			var result = await Parser.CommandParse(Actor.Handle, ConnectionService,
				MarkupText.Plain($"@clone {source}"));
			clone = DBRef.Parse(result.Message.ToPlainText());
		});

		await Assert.That(heard).Contains($"Cloned: Room #{clone!.Value.Number}.");
	}

	/// <summary>
	/// A cloned exit gets no <c>Cloned:</c> line at all: its branch is a <c>do_real_open</c>
	/// (<c>src/create.c:771-773</c>), so the only report is that routine's own "Opened exit #N"
	/// (<c>:159</c>).
	/// </summary>
	[Test]
	public async ValueTask CloneOfAnExitReportsOnlyTheOpenedExit()
	{
		var destination = await LinkableRoom("ClonedExitTarget");
		var exitName = TestIsolationHelpers.GenerateUniqueName("ClonedExit");
		var opened = await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@open {exitName}={destination}"));
		var source = DBRef.Parse(opened.Message.ToPlainText());

		var heard = await ActorHeardWhile(async () => await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@clone {source}={TestIsolationHelpers.GenerateUniqueName("ClonedExitCopy")}")));

		await Assert.That(heard.Any(line => line.StartsWith("Cloned:", StringComparison.Ordinal))).IsFalse()
			.Because("do_clone's exit branch never reaches a Cloned: notify");
		await Assert.That(heard.Any(line => line.StartsWith("Opened exit ", StringComparison.Ordinal))).IsTrue();
	}

	/// <summary>
	/// <c>if (!AreQuiet(player, thing)) notify(player, T("Name set."))</c> (<c>src/set.c:153-154</c>).
	/// <c>@name</c> confirmed nothing at all, so softcode that waits on the line never saw it.
	/// </summary>
	[Test]
	public async ValueTask NameConfirmsWithNameSet()
	{
		var target = await CreateFixtureThing("NameSetTarget");

		var heard = await ActorHeardWhile(async () => await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@name {target}={TestIsolationHelpers.GenerateUniqueName("NameSetNew")}")));

		await Assert.That(heard).Contains("Name set.");
	}

	/// <summary>
	/// <c>AreQuiet(x, y)</c> is <c>Quiet(x) || (Quiet(y) &amp;&amp; Owner(y) == x)</c>
	/// (<c>hdrs/dbdefs.h:198</c>), so a QUIET object the renamer owns swallows the confirmation.
	/// </summary>
	[Test]
	public async ValueTask NameOfAQuietObjectConfirmsNothing()
	{
		var target = await CreateFixtureThing("NameSetQuiet");
		await Parser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@set {target}=QUIET"));

		var heard = await ActorHeardWhile(async () => await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@name {target}={TestIsolationHelpers.GenerateUniqueName("NameSetQuietNew")}")));

		await Assert.That(heard).DoesNotContain("Name set.");
	}

	/// <summary>
	/// <c>do_link</c>'s room branch says <c>Dropto set.</c> (<c>src/create.c:439</c>) and
	/// <c>do_unlink</c>'s says <c>Dropto removed.</c> (<c>:281</c>) — one word, not the hyphenated
	/// "Drop-to" SharpMUSH printed.
	/// </summary>
	[Test]
	public async ValueTask RoomDropToUsesPennMUSHsSpelling()
	{
		var here = (await (await Mediator.Send(new GetObjectNodeQuery(Actor.DbRef))).Expect<AnySharpObject>().Where())
			.Object().DBRef;
		var destination = await LinkableRoom("DroptoSpelling");

		var linked = await ActorHeardWhile(async () => await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@link {here}={destination}")));
		await Assert.That(linked).Contains("Dropto set.");

		var unlinked = await ActorHeardWhile(async () => await Parser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@unlink {here}")));
		await Assert.That(unlinked).Contains("Dropto removed.");
	}
}
