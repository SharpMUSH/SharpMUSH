using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
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

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task ZoneGetNoZone()
	{
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncTest1"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Clear any zone the object may have inherited from the creator, ensuring isolation
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj.Known));

		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneGetNoZone))]
	public async Task ZoneGetWithZone()
	{
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncTest2"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var resultDbRef = DBRef.Parse(result.ToPlainText()!);

		await Assert.That(resultDbRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ZoneGetWithZone))]
	public async Task ZoneSetWithFunction()
	{
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncSetMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		// clearing any inherited zone for clean isolation
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncSetTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj.Known));

		// Set the zone via function (requires side effects enabled)
		var setResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})")))?.Message!;

		// zone() with 2 args returns empty string on success
		await Assert.That(setResult.ToPlainText()).IsEqualTo("");

		var getResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var resultDbRef = DBRef.Parse(getResult.ToPlainText()!);

		await Assert.That(resultDbRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ZoneSetWithFunction))]
	public async Task ZoneClearWithFunction()
	{
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncClearMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncClearTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})"));

		// Clear the zone using "none"
		var clearResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},none)")))?.Message!;
		await Assert.That(clearResult.ToPlainText()).IsEqualTo("");

		var getResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		await Assert.That(getResult.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneClearWithFunction))]
	public async Task ZoneInvalidObject()
	{
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(#99999)")))?.Message!;

		await Assert.That(result.ToPlainText()).Matches("^#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneInvalidObject))]
	public async Task ZoneNoPermissionToExamine()
	{
		// Create an object that player can examine (they created it)
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZonePermTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Clear any inherited zone for clean isolation
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj.Known));

		// Player can examine their own objects, so this should work
		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;

		// Should return #-1 (no zone) rather than permission denied
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneNoPermissionToExamine))]
	public async Task ZoneOnPlayer()
	{
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(%#)")))?.Message!;

		// #-1 (no zone) or a zone dbref
		await Assert.That(result.ToPlainText()).Matches("^(#-1|#[0-9]+:[0-9]+)$");
	}

	[Test]
	[DependsOn(nameof(ZoneOnPlayer))]
	public async Task ZoneOnRoom()
	{
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(%l)")))?.Message!;

		// #-1 (no zone) or a zone dbref
		await Assert.That(result.ToPlainText()).Matches("^(#-1|#[0-9]+:[0-9]+)$");
	}

	[Test]
	[DependsOn(nameof(ZoneOnRoom))]
	public async Task ZoneChainTest()
	{
		// Create a hierarchy: Zone -> Object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChainZoneMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChainZonedObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Clear any inherited zones on both objects for clean isolation
		var zoneObj = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(zoneObj.Known));
		var theObj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(theObj.Known));

		await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})"));

		var objZone = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var objZoneDbRef = DBRef.Parse(objZone.ToPlainText()!);
		await Assert.That(objZoneDbRef.Number).IsEqualTo(zoneDbRef.Number);

		// Verify zone master itself has no zone (should be #-1)
		var zoneOfZone = (await FunctionParser.FunctionParse(MModule.single($"zone({zoneDbRef})")))?.Message!;
		await Assert.That(zoneOfZone.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneChainTest))]
	public async Task ZfindListsObjectsInZone()
	{
		// Create a zone master and clear any inherited zone
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZfindTestZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObj = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(zoneObj.Known));

		// Create multiple objects in the zone, clearing inherited zones first
		var obj1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZfindObj1"));
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = await Mediator.Send(new GetObjectNodeQuery(obj1DbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj1.Known));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {obj1DbRef}={zoneDbRef}"));

		var obj2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZfindObj2"));
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = await Mediator.Send(new GetObjectNodeQuery(obj2DbRef));
		await Mediator.Send(new UnsetObjectZoneCommand(obj2.Known));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {obj2DbRef}={zoneDbRef}"));

		var result = (await FunctionParser.FunctionParse(MModule.single($"zfind({zoneDbRef})")))?.Message!;
		var resultText = result.ToPlainText()!;

		await Assert.That(resultText).IsNotEmpty();
		await Assert.That(resultText).Contains($"{obj1DbRef.Number}");
		await Assert.That(resultText).Contains($"{obj2DbRef.Number}");
	}

	[Test]
	[DependsOn(nameof(ZfindListsObjectsInZone))]
	public async Task ZoneHierarchyTraversal()
	{
		// Create a zone hierarchy: ZoneA <- ZoneB <- Object
		// where ZoneB has ZoneA as its zone, and Object has ZoneB as its zone

		var zoneAResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneHierarchyA"));
		var zoneADbRef = DBRef.Parse(zoneAResult.Message!.ToPlainText()!);

		var zoneBResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneHierarchyB"));
		var zoneBDbRef = DBRef.Parse(zoneBResult.Message!.ToPlainText()!);

		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneHierarchyObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {zoneBDbRef}={zoneADbRef}"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneBDbRef}"));

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
	[DependsOn(nameof(ZoneHierarchyTraversal))]
	public async Task ZoneAttributeInheritance()
	{
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneAttrMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TEST_ZONE_ATTR {zoneDbRef}=Zone Master Value"));

		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneAttrObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// hasattrp checks parents/zones
		var hasAttr = (await FunctionParser.FunctionParse(MModule.single($"hasattrp({objDbRef},TEST_ZONE_ATTR)")))?.Message!;
		await Assert.That(hasAttr.ToPlainText()).IsEqualTo("1");

		// Directly test AttributeService to verify zone attribute inheritance
		var executor = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
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
	[DependsOn(nameof(ZoneAttributeInheritance))]
	public async Task ZoneAttributeInheritanceWithParent()
	{
		// parent attributes take precedence over zone attributes

		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneParentPrecedenceZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&ZONE_PREC_TEST {zoneDbRef}=From Zone"));

		var parentResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneParentPrecedenceParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&ZONE_PREC_TEST {parentDbRef}=From Parent"));

		var childResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneParentPrecedenceChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);

		var setParentResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		var setViaFunction = (await FunctionParser.FunctionParse(MModule.single($"parent({childDbRef},{parentDbRef})")))?.Message!;

		var childFromDB = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentFromDB = await childFromDB.Known.Object().Parent.WithCancellation(CancellationToken.None);

		await Assert.That(parentFromDB.IsNone).IsFalse();
		await Assert.That(parentFromDB.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);

		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {childDbRef}={zoneDbRef}"));

		var childZoneFromDB = await childFromDB.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(childZoneFromDB.IsNone).IsFalse();
		await Assert.That(childZoneFromDB.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);

		// Test attribute inheritance using get_eval which checks parent and zone chains
		// Parent attributes should take precedence over zone attributes
		var childAttrValue = (await FunctionParser.FunctionParse(MModule.single($"get_eval({childDbRef}/ZONE_PREC_TEST)")))?.Message!;
		await Assert.That(childAttrValue.ToPlainText()).IsEqualTo("From Parent");
	}

	[Test]
	[DependsOn(nameof(ZoneAttributeInheritanceWithParent))]
	public async Task ZoneAttributeInheritanceParentHasDifferentZone()
	{
		// Test that each parent can have a different zone
		// Lookup order: child -> child's zone -> parent -> parent's zone

		var childZoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChildZoneMaster"));
		var childZoneDbRef = DBRef.Parse(childZoneResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&CHILD_ZONE_ATTR {childZoneDbRef}=From Child Zone"));

		var parentZoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ParentZoneMaster"));
		var parentZoneDbRef = DBRef.Parse(parentZoneResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&PARENT_ZONE_ATTR {parentZoneDbRef}=From Parent Zone"));

		var parentResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MultiZoneParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {parentDbRef}={parentZoneDbRef}"));

		var childResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MultiZoneChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {childDbRef}={childZoneDbRef}"));

		var executor = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
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