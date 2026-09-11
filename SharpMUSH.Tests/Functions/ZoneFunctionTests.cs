using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class ZoneFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser
	{
		get
		{
			var parser = WebAppFactoryArg.FunctionParserFor(Actor.DbRef);
			return parser.FromState(parser.CurrentState with { Handle = Actor.Handle });
		}
	}
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParserFor(Actor.DbRef, Actor.Handle);
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestIsolationHelpers.TestPlayer Actor { get; set; } = null!;

	[Before(Test)]
	public async Task SetUpActor()
	{
		Actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ZoneActor");
		var actor = (await Mediator.Send(new GetObjectNodeQuery(Actor.DbRef))).AsPlayer;
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(actor, wizard!))).IsTrue();
		var roomId = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ZoneRoom"), actor));
		var room = (await Mediator.Send(new GetObjectNodeQuery(roomId))).AsRoom;
		var origin = await actor.Location.WithCancellation(CancellationToken.None);
		await Mediator.Send(new MoveObjectCommand(actor, room, origin.Object().DBRef, IsSilent: true));
	}

	[After(Test)]
	public async Task DisconnectActor()
	{
		if (Actor is not null)
			await ConnectionService.Disconnect(Actor.Handle);
	}

	private async ValueTask<CallState> CreateFixtureAsync(string name)
	{
		// Graph behavior is independent of the production per-command deadline.
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var fixtureName = TestIsolationHelpers.GenerateUniqueName(name);
		var notificationStart = WebAppFactoryArg.Notifications.CountFor(Actor.DbRef);
		var result = await CommandParser.CommandParse(Actor.Handle, ConnectionService,
			MarkupText.Plain($"@create {fixtureName}"));
		var output = result.Message?.ToPlainText();
		var notifications = string.Join(" | ", WebAppFactoryArg.Notifications.For(Actor.DbRef).Skip(notificationStart));
		await Assert.That(DBRef.TryParse(output, out var created)).IsTrue()
			.Because($"@create {fixtureName} returned '{output}'; actor {Actor.DbRef}; notifications: {notifications}");
		await Assert.That(created!.Value.Number).IsGreaterThan(0);
		var persisted = await Mediator.Send(new GetObjectNodeQuery(created.Value));
		await Assert.That(persisted.IsNone).IsFalse()
			.Because($"@create {fixtureName} returned {created}; actor {Actor.DbRef}; notifications: {notifications}");
		return result;
	}

	[Test]
	public async Task ZoneGetNoZone()
	{
		var objResult = await CreateFixtureAsync("ZoneFuncTest1");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Clear any zone the object may have inherited from the creator, ensuring isolation
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj.Known));

		var result = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task ZoneGetWithZone()
	{
		var zoneResult = await CreateFixtureAsync("ZoneFuncMaster");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await CreateFixtureAsync("ZoneFuncTest2");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		var result = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef})")))?.Message!;
		await Assert.That(DBRef.TryParse(result.ToPlainText(), out var resultDbRef)).IsTrue()
			.Because($"zone({objDbRef}) should return {zoneDbRef}; received '{result.ToPlainText()}'; " +
				$"actor notifications: {string.Join(" | ", WebAppFactoryArg.Notifications.For(Actor.DbRef))}");

		await Assert.That(resultDbRef!.Value.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async Task ZoneSetWithFunction()
	{
		var zoneResult = await CreateFixtureAsync("ZoneFuncSetMaster");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		// clearing any inherited zone for clean isolation
		var objResult = await CreateFixtureAsync("ZoneFuncSetTest");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj.Known));

		// Set the zone via function (requires side effects enabled)
		var setResult = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef},{zoneDbRef})")))?.Message!;

		// zone() with 2 args returns empty string on success
		await Assert.That(setResult.ToPlainText()).IsEqualTo("");

		var getResult = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef})")))?.Message!;
		var resultDbRef = DBRef.Parse(getResult.ToPlainText()!);

		await Assert.That(resultDbRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async Task ZoneClearWithFunction()
	{
		var zoneResult = await CreateFixtureAsync("ZoneFuncClearMaster");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await CreateFixtureAsync("ZoneFuncClearTest");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef},{zoneDbRef})"));

		// Clear the zone using "none"
		var clearResult = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef},none)")))?.Message!;
		await Assert.That(clearResult.ToPlainText()).IsEqualTo("");

		var getResult = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef})")))?.Message!;
		await Assert.That(getResult.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task ZoneInvalidObject()
	{
		var result = (await FunctionParser.FunctionParse(MarkupText.Plain("zone(#99999)")))?.Message!;

		await Assert.That(result.ToPlainText()).Matches("^#-1");
	}

	[Test]
	public async Task ZoneNoPermissionToExamine()
	{
		// Create an object that player can examine (they created it)
		var objResult = await CreateFixtureAsync("ZonePermTest");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Clear any inherited zone for clean isolation
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj.Known));

		// Player can examine their own objects, so this should work
		var result = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef})")))?.Message!;

		// Should return #-1 (no zone) rather than permission denied
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task ZoneOnPlayer()
	{
		var result = (await FunctionParser.FunctionParse(MarkupText.Plain("zone(%#)")))?.Message!;

		// The private fixture has no zone.
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task ZoneOnRoom()
	{
		var result = (await FunctionParser.FunctionParse(MarkupText.Plain("zone(%l)")))?.Message!;

		// The private fixture has no zone.
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task ZoneChainTest()
	{
		// Create a hierarchy: Zone -> Object
		var zoneResult = await CreateFixtureAsync("ChainZoneMaster");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await CreateFixtureAsync("ChainZonedObj");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Clear any inherited zones on both objects for clean isolation
		var zoneObj = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(zoneObj.Known));
		var theObj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(theObj.Known));

		await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef},{zoneDbRef})"));

		var objZone = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({objDbRef})")))?.Message!;
		var objZoneDbRef = DBRef.Parse(objZone.ToPlainText()!);
		await Assert.That(objZoneDbRef.Number).IsEqualTo(zoneDbRef.Number);

		// Verify zone master itself has no zone (should be #-1)
		var zoneOfZone = (await FunctionParser.FunctionParse(MarkupText.Plain($"zone({zoneDbRef})")))?.Message!;
		await Assert.That(zoneOfZone.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task ZfindListsObjectsInZone()
	{
		// Create a zone master and clear any inherited zone
		var zoneResult = await CreateFixtureAsync("ZfindTestZone");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObj = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(zoneObj.Known));

		// Create multiple objects in the zone, clearing inherited zones first
		var obj1Result = await CreateFixtureAsync("ZfindObj1");
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = await Mediator.Send(new GetObjectNodeQuery(obj1DbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj1.Known));
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {obj1DbRef}={zoneDbRef}"));

		var obj2Result = await CreateFixtureAsync("ZfindObj2");
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = await Mediator.Send(new GetObjectNodeQuery(obj2DbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj2.Known));
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {obj2DbRef}={zoneDbRef}"));

		var result = (await FunctionParser.FunctionParse(MarkupText.Plain($"zfind({zoneDbRef})")))?.Message!;
		var resultText = result.ToPlainText()!;

		await Assert.That(resultText).IsNotEmpty();
		await Assert.That(resultText).Contains($"{obj1DbRef.Number}");
		await Assert.That(resultText).Contains($"{obj2DbRef.Number}");
	}

	[Test]
	public async Task ZoneHierarchyTraversal()
	{
		// Create a zone hierarchy: ZoneA <- ZoneB <- Object
		// where ZoneB has ZoneA as its zone, and Object has ZoneB as its zone

		var zoneAResult = await CreateFixtureAsync("ZoneHierarchyA");
		var zoneADbRef = DBRef.Parse(zoneAResult.Message!.ToPlainText()!);

		var zoneBResult = await CreateFixtureAsync("ZoneHierarchyB");
		var zoneBDbRef = DBRef.Parse(zoneBResult.Message!.ToPlainText()!);

		var objResult = await CreateFixtureAsync("ZoneHierarchyObj");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {zoneBDbRef}={zoneADbRef}"));
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneBDbRef}"));

		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zoneChain = new List<int>();

		await foreach (var zone in obj.Known.GetZoneChain())
		{
			zoneChain.Add(zone.Object().DBRef.Number);
		}

		await Assert.That(zoneChain).Contains(zoneBDbRef.Number);
		await Assert.That(zoneChain).Contains(zoneADbRef.Number);
		await Assert.That(zoneChain.Count).IsEqualTo(2);

		// First should be ZoneB (immediate zone), then ZoneA
		await Assert.That(zoneChain[0]).IsEqualTo(zoneBDbRef.Number);
		await Assert.That(zoneChain[1]).IsEqualTo(zoneADbRef.Number);
	}

	[Test]
	public async Task ZoneAttributeInheritance()
	{
		var zoneResult = await CreateFixtureAsync("ZoneAttrMaster");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&TEST_ZONE_ATTR {zoneDbRef}=Zone Master Value"));

		var objResult = await CreateFixtureAsync("ZoneAttrObj");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		var stored = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(stored.IsNone).IsFalse();
		var storedZone = await stored.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(storedZone.IsNone).IsFalse();
		await Assert.That(storedZone.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
		var zoneAttribute = await Mediator.CreateStream(new GetAttributeQuery(zoneDbRef, ["TEST_ZONE_ATTR"])).SingleAsync();
		await Assert.That(zoneAttribute.Value.ToPlainText()).IsEqualTo("Zone Master Value");

		// hasattrp checks parents/zones
		var hasAttr = (await FunctionParser.FunctionParse(MarkupText.Plain($"hasattrp({objDbRef},TEST_ZONE_ATTR)")))?.Message!;
		await Assert.That(hasAttr.ToPlainText()).IsEqualTo("1");

		// Directly test AttributeService to verify zone attribute inheritance
		var executor = await Mediator.Send(new GetObjectNodeQuery(Actor.DbRef));
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

		var maybeAttr = await attributeService.GetAttributeAsync(
			executor.Known,
			obj.Known,
			"TEST_ZONE_ATTR",
			IAttributeService.AttributeMode.Read,
			parent: true);

		await Assert.That(maybeAttr.IsAttribute).IsTrue();
		await Assert.That(maybeAttr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("Zone Master Value");
	}

	[Test]
	public async Task ZoneAttributeInheritanceWithParent()
	{
		// parent attributes take precedence over zone attributes

		var zoneResult = await CreateFixtureAsync("ZoneParentPrecedenceZone");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&ZONE_PREC_TEST {zoneDbRef}=From Zone"));

		var parentResult = await CreateFixtureAsync("ZoneParentPrecedenceParent");
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&ZONE_PREC_TEST {parentDbRef}=From Parent"));

		var childResult = await CreateFixtureAsync("ZoneParentPrecedenceChild");
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		var setParentResult = await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));

		var setViaFunction = (await FunctionParser.FunctionParse(MarkupText.Plain($"parent({childDbRef},{parentDbRef})")))?.Message!;

		var childFromDB = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentFromDB = await childFromDB.Known.Object().Parent.WithCancellation(CancellationToken.None);

		await Assert.That(parentFromDB.IsNone).IsFalse();
		await Assert.That(parentFromDB.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);

		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {childDbRef}={zoneDbRef}"));

		var childZoneFromDB = await childFromDB.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(childZoneFromDB.IsNone).IsFalse();
		await Assert.That(childZoneFromDB.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);

		// Test attribute inheritance using get_eval which checks parent and zone chains
		// Parent attributes should take precedence over zone attributes
		var childAttrValue = (await FunctionParser.FunctionParse(MarkupText.Plain($"get_eval({childDbRef}/ZONE_PREC_TEST)")))?.Message!;
		await Assert.That(childAttrValue.ToPlainText()).IsEqualTo("From Parent");
	}

	[Test]
	public async Task ZoneAttributeInheritanceParentHasDifferentZone()
	{
		// Test that each parent can have a different zone
		// Lookup order: child -> child's zone -> parent -> parent's zone

		var childZoneResult = await CreateFixtureAsync("ChildZoneMaster");
		var childZoneDbRef = DBRef.Parse(childZoneResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&CHILD_ZONE_ATTR {childZoneDbRef}=From Child Zone"));

		var parentZoneResult = await CreateFixtureAsync("ParentZoneMaster");
		var parentZoneDbRef = DBRef.Parse(parentZoneResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"&PARENT_ZONE_ATTR {parentZoneDbRef}=From Parent Zone"));

		var parentResult = await CreateFixtureAsync("MultiZoneParent");
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {parentDbRef}={parentZoneDbRef}"));

		var childResult = await CreateFixtureAsync("MultiZoneChild");
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@parent {childDbRef}={parentDbRef}"));
		await CommandParser.CommandParse(Actor.Handle, ConnectionService, MarkupText.Plain($"@chzone {childDbRef}={childZoneDbRef}"));

		var executor = await Mediator.Send(new GetObjectNodeQuery(Actor.DbRef));
		var child = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

		var childZoneAttr = await attributeService.GetAttributeAsync(
			executor.Known,
			child.Known,
			"CHILD_ZONE_ATTR",
			IAttributeService.AttributeMode.Read,
			parent: true);

		await Assert.That(childZoneAttr.IsAttribute).IsTrue();
		await Assert.That(childZoneAttr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("From Child Zone");

		// Should inherit from parent's zone (after checking child and child's zone)
		var parentZoneAttr = await attributeService.GetAttributeAsync(
			executor.Known,
			child.Known,
			"PARENT_ZONE_ATTR",
			IAttributeService.AttributeMode.Read,
			parent: true);

		await Assert.That(parentZoneAttr.IsAttribute).IsTrue();
		await Assert.That(parentZoneAttr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("From Parent Zone");
	}
}