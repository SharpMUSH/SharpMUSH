using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

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
		var obj1Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "TestObj1");
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = (await Mediator.Send(new GetObjectNodeQuery(obj1DbRef))).Expect<AnySharpObject>();

		var obj2Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "TestObj2");
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = (await Mediator.Send(new GetObjectNodeQuery(obj2DbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectParentCommand(obj1, obj2));

		// Try to set obj2's parent to obj1 (would create a cycle)
		var result = await ManipulateService.SetParent(obj1, obj2, obj1, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask DirectZoneCycle_ShouldFail()
	{
		var zone1Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "TestZone1");
		var zone1DbRef = DBRef.Parse(zone1Result.Message!.ToPlainText()!);
		var zone1 = (await Mediator.Send(new GetObjectNodeQuery(zone1DbRef))).Expect<AnySharpObject>();

		var zone2Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "TestZone2");
		var zone2DbRef = DBRef.Parse(zone2Result.Message!.ToPlainText()!);
		var zone2 = (await Mediator.Send(new GetObjectNodeQuery(zone2DbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectZoneCommand(zone1, zone2));

		// Try to set zone2's zone to zone1 (would create a cycle)
		var result = await ManipulateService.SetZone(zone1, zone2, zone1, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ParentWithZoneCycle_ShouldFail()
	{
		var objAResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "MixedCycleA");
		var objADbRef = DBRef.Parse(objAResult.Message!.ToPlainText()!);
		var objA = (await Mediator.Send(new GetObjectNodeQuery(objADbRef))).Expect<AnySharpObject>();

		var objBResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "MixedCycleB");
		var objBDbRef = DBRef.Parse(objBResult.Message!.ToPlainText()!);
		var objB = (await Mediator.Send(new GetObjectNodeQuery(objBDbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectParentCommand(objA, objB));

		// Try to set objB's zone to objA (would create a cycle: A -> parent B -> zone A)
		var result = await ManipulateService.SetZone(objA, objB, objA, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ZoneWithParentCycle_ShouldFail()
	{
		var objXResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "MixedCycleX");
		var objXDbRef = DBRef.Parse(objXResult.Message!.ToPlainText()!);
		var objX = (await Mediator.Send(new GetObjectNodeQuery(objXDbRef))).Expect<AnySharpObject>();

		var objYResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "MixedCycleY");
		var objYDbRef = DBRef.Parse(objYResult.Message!.ToPlainText()!);
		var objY = (await Mediator.Send(new GetObjectNodeQuery(objYDbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectZoneCommand(objX, objY));

		// Try to set objY's parent to objX (would create a cycle: X -> zone Y -> parent X)
		var result = await ManipulateService.SetParent(objX, objY, objX, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask MultiHopParentZoneCycle_ShouldFail()
	{
		var obj1Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "MultiHop1");
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = (await Mediator.Send(new GetObjectNodeQuery(obj1DbRef))).Expect<AnySharpObject>();

		var obj2Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "MultiHop2");
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = (await Mediator.Send(new GetObjectNodeQuery(obj2DbRef))).Expect<AnySharpObject>();

		var obj3Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "MultiHop3");
		var obj3DbRef = DBRef.Parse(obj3Result.Message!.ToPlainText()!);
		var obj3 = (await Mediator.Send(new GetObjectNodeQuery(obj3DbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectParentCommand(obj1, obj2));

		await Mediator.Send(new SetObjectZoneCommand(obj2, obj3));

		// Try to set obj3 -> parent obj1 (would create a cycle: 1 -> parent 2 -> zone 3 -> parent 1)
		var result = await ManipulateService.SetParent(obj1, obj3, obj1, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ValidParentAndZone_ShouldSucceed()
	{
		var parentResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "ValidParent");
		var parentDbRef = DBRef.Parse(parentResult.Message!.ToPlainText()!);
		var parent = (await Mediator.Send(new GetObjectNodeQuery(parentDbRef))).Expect<AnySharpObject>();

		var zoneResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "ValidZone");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zone = (await Mediator.Send(new GetObjectNodeQuery(zoneDbRef))).Expect<AnySharpObject>();

		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "ValidObject");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		var parentResult2 = await ManipulateService.SetParent(obj, obj, parent, false);
		await Assert.That(parentResult2.Message).IsNotNull();

		var zoneResult2 = await ManipulateService.SetZone(obj, obj, zone, false);
		await Assert.That(zoneResult2.Message).IsNotNull();

		var updated = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var objParent = await updated.Object().Parent.WithCancellation(CancellationToken.None);
		var objZone = await updated.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(objParent.IsNone).IsFalse();
		await Assert.That(objParent.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(parentDbRef.Number);
		await Assert.That(objZone.IsNone).IsFalse();
		await Assert.That(objZone.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async ValueTask SelfParent_ShouldFail()
	{
		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "SelfParentTest");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		// Try to set object as its own parent
		var result = await ManipulateService.SetParent(obj, obj, obj, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask SelfZone_ShouldFail()
	{
		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "SelfZoneTest");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		// Try to set object as its own zone
		var result = await ManipulateService.SetZone(obj, obj, obj, false);

		await Assert.That(result.Message).IsNotNull();
		var message = result.Message!.ToPlainText()!;
		await Assert.That(message).Contains("LOOP");
	}

	[Test]
	public async ValueTask ChzoneCommand_WithCycle_ShouldFail()
	{
		var zone1Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "ChzoneCycle1");
		var zone1DbRefParsed = DBRef.Parse(zone1Result.Message!.ToPlainText()!);

		var zone2Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "ChzoneCycle2");
		var zone2DbRefParsed = DBRef.Parse(zone2Result.Message!.ToPlainText()!);

		// Clear any inherited zones for clean isolation
		var z1 = (await Mediator.Send(new GetObjectNodeQuery(zone1DbRefParsed))).Expect<AnySharpObject>();
		await Mediator.Send(new UnsetObjectZoneCommand(z1));
		var z2 = (await Mediator.Send(new GetObjectNodeQuery(zone2DbRefParsed))).Expect<AnySharpObject>();
		await Mediator.Send(new UnsetObjectZoneCommand(z2));

		TestDiagnostics.WriteLine($"Created objects: #{zone1DbRefParsed.Number} and #{zone2DbRefParsed.Number}");

		// Use number-only DBRefs for commands (to avoid parser issues with timestamps)
		var zone1Num = zone1DbRefParsed.Number;
		var zone2Num = zone2DbRefParsed.Number;

		var firstCommand = $"@chzone #{zone1Num}=#{zone2Num}";
		TestDiagnostics.WriteLine($"First command: {firstCommand}");
		var firstResult = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(firstCommand));
		TestDiagnostics.WriteLine($"First @chzone result: '{firstResult.Message?.ToPlainText()}'");

		var zone1Obj = (await Mediator.Send(new GetObjectNodeQuery(zone1DbRefParsed))).Expect<AnySharpObject>();
		var zone1Zone = await zone1Obj.Object().Zone.WithCancellation(CancellationToken.None);
		TestDiagnostics.WriteLine($"zone1.zone IsNone: {zone1Zone.IsNone}");
		if (zone1Zone is AnySharpObject zone1ZoneObject)
		{
			TestDiagnostics.WriteLine($"zone1.zone = #{zone1ZoneObject.Object().DBRef.Number}");
		}

		// Try to set zone2's zone to zone1 (should fail with cycle detection)
		var secondCommand = $"@chzone #{zone2Num}=#{zone1Num}";
		TestDiagnostics.WriteLine($"Second command: {secondCommand}");
		var result = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(secondCommand));
		TestDiagnostics.WriteLine($"Second @chzone result: '{result.Message?.ToPlainText()}'");

		// Check if zone2.zone was actually set (it shouldn't be! - cycle prevention)
		var zone2Obj = (await Mediator.Send(new GetObjectNodeQuery(zone2DbRefParsed))).Expect<AnySharpObject>();
		var zone2Zone = await zone2Obj.Object().Zone.WithCancellation(CancellationToken.None);
		TestDiagnostics.WriteLine($"zone2.zone IsNone: {zone2Zone.IsNone}");
		if (zone2Zone is AnySharpObject zone2ZoneObject)
		{
			TestDiagnostics.WriteLine($"zone2.zone = #{zone2ZoneObject.Object().DBRef.Number} (SHOULD NOT BE SET!)");
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
		var zoneResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "SimpleZone");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "SimpleObj");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		var updated = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var objZone = await updated.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(objZone.IsNone).IsFalse();
		await Assert.That(objZone.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async ValueTask DebugChzoneBasic()
	{
		var obj1Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DebugObj1");
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);

		var obj2Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DebugObj2");
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);

		// Clear any inherited zones for clean isolation
		var dbObj1 = (await Mediator.Send(new GetObjectNodeQuery(obj1DbRef))).Expect<AnySharpObject>();
		await Mediator.Send(new UnsetObjectZoneCommand(dbObj1));
		var dbObj2 = (await Mediator.Send(new GetObjectNodeQuery(obj2DbRef))).Expect<AnySharpObject>();
		await Mediator.Send(new UnsetObjectZoneCommand(dbObj2));

		var chzoneResult = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {obj1DbRef}={obj2DbRef}"));
		TestDiagnostics.WriteLine($"Chzone result: '{chzoneResult.Message?.ToPlainText()}'");

		var obj1 = (await Mediator.Send(new GetObjectNodeQuery(obj1DbRef))).Expect<AnySharpObject>();
		var obj1Zone = await obj1.Object().Zone.WithCancellation(CancellationToken.None);

		TestDiagnostics.WriteLine($"obj1.zone IsNone: {obj1Zone.IsNone}");
		if (obj1Zone is AnySharpObject obj1ZoneObject)
		{
			TestDiagnostics.WriteLine($"obj1.zone DBRef: {obj1ZoneObject.Object().DBRef}");
			TestDiagnostics.WriteLine($"Expected: {obj2DbRef}");
		}

		await Assert.That(obj1Zone.IsNone).IsFalse();
		await Assert.That(obj1Zone.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(obj2DbRef.Number);
	}
}
