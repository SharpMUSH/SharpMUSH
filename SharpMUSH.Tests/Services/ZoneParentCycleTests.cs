using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class ZoneParentCycleTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IManipulateSharpObjectService ManipulateService => WebAppFactoryArg.Services.GetRequiredService<IManipulateSharpObjectService>();

	[Test]
	public async ValueTask DirectParentCycle_ShouldFail()
	{
		// Create two objects
		var obj1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create TestObj1"));
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = await Mediator.Send(new GetObjectNodeQuery(obj1DbRef));

		var obj2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create TestObj2"));
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = await Mediator.Send(new GetObjectNodeQuery(obj2DbRef));

		// Set obj1's parent to obj2
		await Mediator.Send(new SetObjectParentCommand(obj1.Known, obj2.Known));

		// Try to set obj2's parent to obj1 (would create a cycle)
		var result = await ManipulateService.SetParent(obj1.Known, obj2.Known, obj1.Known, false);

		// Should fail with parent loop error
		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask DirectZoneCycle_ShouldFail()
	{
		// Create two objects
		var zone1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create TestZone1"));
		var zone1DbRef = DBRef.Parse(zone1Result.Message!.ToPlainText()!);
		var zone1 = await Mediator.Send(new GetObjectNodeQuery(zone1DbRef));

		var zone2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create TestZone2"));
		var zone2DbRef = DBRef.Parse(zone2Result.Message!.ToPlainText()!);
		var zone2 = await Mediator.Send(new GetObjectNodeQuery(zone2DbRef));

		// Set zone1's zone to zone2
		await Mediator.Send(new SetObjectZoneCommand(zone1.Known, zone2.Known));

		// Try to set zone2's zone to zone1 (would create a cycle)
		var result = await ManipulateService.SetZone(zone1.Known, zone2.Known, zone1.Known, false);

		// Should fail with zone loop error
		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ParentWithZoneCycle_ShouldFail()
	{
		// Create two objects
		var objAResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MixedCycleA"));
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);
		var objA = await Mediator.Send(new GetObjectNodeQuery(objADbRef));

		var objBResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MixedCycleB"));
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);
		var objB = await Mediator.Send(new GetObjectNodeQuery(objBDbRef));

		// Set objA's parent to objB
		await Mediator.Send(new SetObjectParentCommand(objA.Known, objB.Known));

		// Try to set objB's zone to objA (would create a cycle: A -> parent B -> zone A)
		var result = await ManipulateService.SetZone(objA.Known, objB.Known, objA.Known, false);

		// Should fail with zone loop error
		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ZoneWithParentCycle_ShouldFail()
	{
		// Create two objects
		var objXResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MixedCycleX"));
		var objXDbRef = DBRef.Parse(objXResult.Message!.ToPlainText()!);
		var objX = await Mediator.Send(new GetObjectNodeQuery(objXDbRef));

		var objYResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MixedCycleY"));
		var objYDbRef = DBRef.Parse(objYResult.Message!.ToPlainText()!);
		var objY = await Mediator.Send(new GetObjectNodeQuery(objYDbRef));

		// Set objX's zone to objY
		await Mediator.Send(new SetObjectZoneCommand(objX.Known, objY.Known));

		// Try to set objY's parent to objX (would create a cycle: X -> zone Y -> parent X)
		var result = await ManipulateService.SetParent(objX.Known, objY.Known, objX.Known, false);

		// Should fail with parent loop error
		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask MultiHopParentZoneCycle_ShouldFail()
	{
		// Create three objects
		var obj1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MultiHop1"));
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = await Mediator.Send(new GetObjectNodeQuery(obj1DbRef));

		var obj2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MultiHop2"));
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = await Mediator.Send(new GetObjectNodeQuery(obj2DbRef));

		var obj3Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create MultiHop3"));
		var obj3DbRef = DBRef.Parse(obj3Result.Message!.ToPlainText()!);
		var obj3 = await Mediator.Send(new GetObjectNodeQuery(obj3DbRef));

		// Set obj1 -> parent obj2
		await Mediator.Send(new SetObjectParentCommand(obj1.Known, obj2.Known));

		// Set obj2 -> zone obj3
		await Mediator.Send(new SetObjectZoneCommand(obj2.Known, obj3.Known));

		// Try to set obj3 -> parent obj1 (would create a cycle: 1 -> parent 2 -> zone 3 -> parent 1)
		var result = await ManipulateService.SetParent(obj1.Known, obj3.Known, obj1.Known, false);

		// Should fail with parent loop error
		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ValidParentAndZone_ShouldSucceed()
	{
		// Create three objects
		var parentResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ValidParent"));
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		var parent = await Mediator.Send(new GetObjectNodeQuery(parentDbRef));

		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ValidZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zone = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));

		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ValidObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Set obj's parent to parent
		var parentResult2 = await ManipulateService.SetParent(obj.Known, obj.Known, parent.Known, false);
		await Assert.That(parentResult2.Message).IsNotNull();

		// Set obj's zone to zone
		var zoneResult2 = await ManipulateService.SetZone(obj.Known, obj.Known, zone.Known, false);
		await Assert.That(zoneResult2.Message).IsNotNull();

		// Verify both are set
		var updated = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objParent = await updated.Known.Object().Parent.WithCancellation(CancellationToken.None);
		var objZone = await updated.Known.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(objParent.IsNone).IsFalse();
		await Assert.That(objParent.Known.Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
		await Assert.That(objZone.IsNone).IsFalse();
		await Assert.That(objZone.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async ValueTask SelfParent_ShouldFail()
	{
		// Create object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create SelfParentTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Try to set object as its own parent
		var result = await ManipulateService.SetParent(obj.Known, obj.Known, obj.Known, false);

		// Should fail
		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask SelfZone_ShouldFail()
	{
		// Create object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create SelfZoneTest"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Try to set object as its own zone
		var result = await ManipulateService.SetZone(obj.Known, obj.Known, obj.Known, false);

		// Should fail
		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ChzoneCommand_WithCycle_ShouldFail()
	{
		// Clear player zone to avoid inheritance issues
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create two objects
		var zone1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChzoneCycle1"));
		var zone1DbRefParsed = DBRef.Parse(zone1Result.Message!.ToPlainText()!);

		var zone2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create ChzoneCycle2"));
		var zone2DbRefParsed = DBRef.Parse(zone2Result.Message!.ToPlainText()!);

		Console.WriteLine($"Created objects: #{zone1DbRefParsed.Number} and #{zone2DbRefParsed.Number}");

		// Use number-only DBRefs for commands (to avoid parser issues with timestamps)
		var zone1Num = zone1DbRefParsed.Number;
		var zone2Num = zone2DbRefParsed.Number;

		// Set zone1's zone to zone2
		var firstCommand = $"@chzone #{zone1Num}=#{zone2Num}";
		Console.WriteLine($"First command: {firstCommand}");
		var firstResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single(firstCommand));
		Console.WriteLine($"First @chzone result: '{firstResult.Message?.ToPlainText()}'");
		
		// Verify zone1.zone was set to zone2 (use parsed DBRef for query)
		var zone1Obj = await Mediator.Send(new GetObjectNodeQuery(zone1DbRefParsed));
		var zone1Zone = await zone1Obj.Known.Object().Zone.WithCancellation(CancellationToken.None);
		Console.WriteLine($"zone1.zone IsNone: {zone1Zone.IsNone}");
		if (!zone1Zone.IsNone)
		{
			Console.WriteLine($"zone1.zone = #{zone1Zone.Known.Object().DBRef.Number}");
		}

		// Try to set zone2's zone to zone1 (should fail with cycle detection)
		var secondCommand = $"@chzone #{zone2Num}=#{zone1Num}";
		Console.WriteLine($"Second command: {secondCommand}");
		var result = await CommandParser.CommandParse(1, ConnectionService, MModule.single(secondCommand));
		Console.WriteLine($"Second @chzone result: '{result.Message?.ToPlainText()}'");

		// Check if zone2.zone was actually set (it shouldn't be! - cycle prevention)
		var zone2Obj = await Mediator.Send(new GetObjectNodeQuery(zone2DbRefParsed));
		var zone2Zone = await zone2Obj.Known.Object().Zone.WithCancellation(CancellationToken.None);
		Console.WriteLine($"zone2.zone IsNone: {zone2Zone.IsNone}");
		if (!zone2Zone.IsNone)
		{
			Console.WriteLine($"zone2.zone = #{zone2Zone.Known.Object().DBRef.Number} (SHOULD NOT BE SET!)");
		}

		// The key assertion: zone2's zone should NOT be set (cycle was prevented)
		await Assert.That(zone2Zone.IsNone).IsTrue();
		
		// If the command was executed (not echoed), it should have an error message about cycle
		var message = result.Message?.ToPlainText()?.ToLowerInvariant() ?? "";
		// Only check for cycle message if the command was actually executed (not echoed)
		if (!message.Contains("@chzone"))
		{
			await Assert.That(message.Contains("cycle") || message.Contains("loop")).IsTrue();
		}
	}

	[Test]
	public async ValueTask ChzoneCommand_Simple_ShouldSucceed()
	{
		// Create two objects
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create SimpleZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create SimpleObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		// Set obj's zone to zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// Verify zone was set
		var updated = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objZone = await updated.Known.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(objZone.IsNone).IsFalse();
		await Assert.That(objZone.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async ValueTask DebugChzoneBasic()
	{
		// Clear player zone
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create two objects
		var obj1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DebugObj1"));
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		
		var obj2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DebugObj2"));
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		
		// Set obj1's zone to obj2
		var chzoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@chzone {obj1DbRef}={obj2DbRef}"));
		Console.WriteLine($"Chzone result: '{chzoneResult.Message?.ToPlainText()}'");
		
		// Verify zone was set
		var obj1 = await Mediator.Send(new GetObjectNodeQuery(obj1DbRef));
		var obj1Zone = await obj1.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		Console.WriteLine($"obj1.zone IsNone: {obj1Zone.IsNone}");
		if (!obj1Zone.IsNone)
		{
			Console.WriteLine($"obj1.zone DBRef: {obj1Zone.Known.Object().DBRef}");
			Console.WriteLine($"Expected: {obj2DbRef}");
		}
		
		await Assert.That(obj1Zone.IsNone).IsFalse();
		await Assert.That(obj1Zone.Known.Object().DBRef.Number).IsEqualTo(obj2DbRef.Number);
	}
}
