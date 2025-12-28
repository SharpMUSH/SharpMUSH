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

[NotInParallel]
public class ZoneFunctionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task ZoneGetNoZone()
	{
		// First, ensure the player has no zone (so objects don't inherit one)
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create an object without a zone
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncTest1"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Get zone should return #-1 for objects with no zone
		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneGetNoZone))]
	public async Task ZoneGetWithZone()
	{
		// Create a zone master object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Create an object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncTest2"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set the zone via command
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));
		
		// Get zone should return the zone dbref
		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var resultDbRef = DBRef.Parse(result.ToPlainText()!);
		
		await Assert.That(resultDbRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ZoneGetWithZone))]
	public async Task ZoneSetWithFunction()
	{
		// Ensure player has no zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create a zone master object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncSetMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Create an object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncSetTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set the zone via function (requires side effects enabled)
		var setResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})")))?.Message!;
		
		// zone() with 2 args returns empty string on success
		await Assert.That(setResult.ToPlainText()).IsEqualTo("");
		
		// Verify the zone was actually set
		var getResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		var resultDbRef = DBRef.Parse(getResult.ToPlainText()!);
		
		await Assert.That(resultDbRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(ZoneSetWithFunction))]
	public async Task ZoneClearWithFunction()
	{
		// Create a zone master object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncClearMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Create an object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneFuncClearTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set the zone first
		await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})"));
		
		// Clear the zone using "none"
		var clearResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},none)")))?.Message!;
		await Assert.That(clearResult.ToPlainText()).IsEqualTo("");
		
		// Verify the zone was cleared
		var getResult = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		await Assert.That(getResult.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneClearWithFunction))]
	public async Task ZoneInvalidObject()
	{
		// Try to get zone of invalid object
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(#99999)")))?.Message!;
		
		// Should return an error
		await Assert.That(result.ToPlainText()).Matches("^#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneInvalidObject))]
	public async Task ZoneNoPermissionToExamine()
	{
		// Ensure player has no zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create an object that player can examine (they created it)
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZonePermTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Player can examine their own objects, so this should work
		var result = (await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef})")))?.Message!;
		
		// Should return #-1 (no zone) rather than permission denied
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	[DependsOn(nameof(ZoneNoPermissionToExamine))]
	public async Task ZoneOnPlayer()
	{
		// Test getting zone of current player
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(%#)")))?.Message!;
		
		// Should return #-1 or a zone dbref
		await Assert.That(result.ToPlainText()).Matches("^(#-1|#[0-9]+:[0-9]+)$");
	}

	[Test]
	[DependsOn(nameof(ZoneOnPlayer))]
	public async Task ZoneOnRoom()
	{
		// Test getting zone of current room
		var result = (await FunctionParser.FunctionParse(MModule.single("zone(%l)")))?.Message!;
		
		// Should return #-1 or a zone dbref
		await Assert.That(result.ToPlainText()).Matches("^(#-1|#[0-9]+:[0-9]+)$");
	}

	[Test]
	[DependsOn(nameof(ZoneOnRoom))]
	public async Task ZoneChainTest()
	{
		// Ensure player has no zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create a hierarchy: Zone -> Object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChainZoneMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChainZonedObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		
		// Set zone
		await FunctionParser.FunctionParse(MModule.single($"zone({objDbRef},{zoneDbRef})"));
		
		// Verify object has zone
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
		// Clear player zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create a zone master
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZfindTestZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Create multiple objects in the zone
		var obj1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZfindObj1"));
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {obj1DbRef}={zoneDbRef}"));
		
		var obj2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZfindObj2"));
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {obj2DbRef}={zoneDbRef}"));
		
		// Call zfind to get all objects in the zone
		var result = (await FunctionParser.FunctionParse(MModule.single($"zfind({zoneDbRef})")))?.Message!;
		var resultText = result.ToPlainText()!;
		
		// Should contain both objects (check that result is not empty and contains at least the object references)
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
		
		// Set up hierarchy
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {zoneBDbRef}={zoneADbRef}"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneBDbRef}"));
		
		// Verify the zone chain
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zoneChain = new List<int>();
		
		await foreach (var zone in obj.Known.GetZoneChain())
		{
			zoneChain.Add(zone.Object().DBRef.Number);
		}
		
		// Should have both ZoneB and ZoneA in the chain
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
		// Create a zone master with an attribute
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneAttrMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Set an attribute on the zone master
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TEST_ZONE_ATTR {zoneDbRef}=Zone Master Value"));
		
		// Create an object and zone it to the master
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneAttrObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));
		
		// Get the attribute from the object using hasattrp (checks parents/zones)
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
		// Test that parent attributes take precedence over zone attributes
		
		// Create a zone master with an attribute
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneParentPrecedenceZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		
		// Set attribute on zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&ZONE_PREC_TEST {zoneDbRef}=From Zone"));
		
		// Create a parent object with the same attribute
		var parentResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneParentPrecedenceParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		
		// Set attribute on parent
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&ZONE_PREC_TEST {parentDbRef}=From Parent"));
		
		// Create a child object
		var childResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ZoneParentPrecedenceChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);
		
		// Set parent - check result
		var setParentResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));
		
		// Debug: Check if parent() function can be called with 2 args to set parent
		var setViaFunction = (await FunctionParser.FunctionParse(MModule.single($"parent({childDbRef},{parentDbRef})")))?.Message!;
		
		// Verify parent was set by querying database directly
		var childFromDB = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var parentFromDB = await childFromDB.Known.Object().Parent.WithCancellation(CancellationToken.None);
		
		// Parent must be set for this test to work
		await Assert.That(parentFromDB.IsNone).IsFalse();
		await Assert.That(parentFromDB.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
		
		// Set zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {childDbRef}={zoneDbRef}"));
		
		// Verify zone was set
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
		
		// Create zone for child
		var childZoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChildZoneMaster"));
		var childZoneDbRef = DBRef.Parse(childZoneResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&CHILD_ZONE_ATTR {childZoneDbRef}=From Child Zone"));
		
		// Create zone for parent
		var parentZoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ParentZoneMaster"));
		var parentZoneDbRef = DBRef.Parse(parentZoneResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&PARENT_ZONE_ATTR {parentZoneDbRef}=From Parent Zone"));
		
		// Create parent with its zone
		var parentResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MultiZoneParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {parentDbRef}={parentZoneDbRef}"));
		
		// Create child with parent and different zone
		var childResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MultiZoneChild"));
		var childDbRef = DBRef.Parse(childResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {childDbRef}={childZoneDbRef}"));
		
		// Test with AttributeService directly
		var executor = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		var child = await Mediator.Send(new GetObjectNodeQuery(childDbRef));
		var attributeService = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		
		// Should inherit from child's zone
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