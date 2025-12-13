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

namespace SharpMUSH.Tests.Database;

[NotInParallel]
public class ZoneDatabaseTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async ValueTask SetObjectZone()
	{
		// Create zone master and object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBZoneMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBZonedObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		
		// Set zone via database
		await Database.SetObjectZone(zonedObject.Known, zoneObject.Known, CancellationToken.None);
		
		// Verify zone was set
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		await Assert.That(zone.IsNone).IsFalse();
		await Assert.That(zone.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(SetObjectZone))]
	public async ValueTask UnsetObjectZone()
	{
		// Clear player zone first to ensure clean state
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create zone master and object
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBUnsetZoneMaster"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBUnsetZonedObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		
		// Set zone first
		await Database.SetObjectZone(zonedObject.Known, zoneObject.Known, CancellationToken.None);
		
		// Verify zone was set
		var withZone = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zoneCheck = await withZone.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zoneCheck.IsNone).IsFalse();
		
		// Unset zone
		await Database.UnsetObjectZone(withZone.Known, CancellationToken.None);
		
		// Verify zone was cleared
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		await Assert.That(zone.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(UnsetObjectZone))]
	public async ValueTask UpdateObjectZone()
	{
		// Create two zone masters and an object
		var zone1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBUpdateZone1"));
		var zone1DbRef = DBRef.Parse(zone1Result.Message!.ToPlainText()!);
		var zone1Object = await Mediator.Send(new GetObjectNodeQuery(zone1DbRef));
		
		var zone2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBUpdateZone2"));
		var zone2DbRef = DBRef.Parse(zone2Result.Message!.ToPlainText()!);
		var zone2Object = await Mediator.Send(new GetObjectNodeQuery(zone2DbRef));
		
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBUpdateZonedObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		
		// Set zone to zone1
		await Database.SetObjectZone(zonedObject.Known, zone1Object.Known, CancellationToken.None);
		
		// Verify initial zone
		var withZone1 = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone1Check = await withZone1.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zone1Check.Known.Object().DBRef.Number).IsEqualTo(zone1DbRef.Number);
		
		// Update zone to zone2
		await Database.SetObjectZone(withZone1.Known, zone2Object.Known, CancellationToken.None);
		
		// Verify updated zone
		var withZone2 = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone2Check = await withZone2.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zone2Check.Known.Object().DBRef.Number).IsEqualTo(zone2DbRef.Number);
	}

	[Test]
	[DependsOn(nameof(UpdateObjectZone))]
	public async ValueTask SetObjectZoneToNull()
	{
		// Create object
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBNullZoneObject"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		
		// Set zone to null (should be equivalent to unset)
		await Database.SetObjectZone(zonedObject.Known, null, CancellationToken.None);
		
		// Verify no zone
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		await Assert.That(zone.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SetObjectZoneToNull))]
	public async ValueTask MultipleObjectsSameZone()
	{
		// Create one zone master and multiple objects
		var zoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBSharedZone"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		
		var obj1Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBSharedZoneObj1"));
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = await Mediator.Send(new GetObjectNodeQuery(obj1DbRef));
		
		var obj2Result = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBSharedZoneObj2"));
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = await Mediator.Send(new GetObjectNodeQuery(obj2DbRef));
		
		// Set both objects to same zone
		await Database.SetObjectZone(obj1.Known, zoneObject.Known, CancellationToken.None);
		await Database.SetObjectZone(obj2.Known, zoneObject.Known, CancellationToken.None);
		
		// Verify both have the same zone
		var updated1 = await Mediator.Send(new GetObjectNodeQuery(obj1DbRef));
		var zone1 = await updated1.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		var updated2 = await Mediator.Send(new GetObjectNodeQuery(obj2DbRef));
		var zone2 = await updated2.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		await Assert.That(zone1.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
		await Assert.That(zone2.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(MultipleObjectsSameZone))]
	public async ValueTask ObjectCanBeZone()
	{
		// An object can be both a zone master and be zoned to another zone
		var topZoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBTopZone"));
		var topZoneDbRef = DBRef.Parse(topZoneResult.Message!.ToPlainText()!);
		var topZone = await Mediator.Send(new GetObjectNodeQuery(topZoneDbRef));
		
		var midZoneResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBMidZone"));
		var midZoneDbRef = DBRef.Parse(midZoneResult.Message!.ToPlainText()!);
		var midZone = await Mediator.Send(new GetObjectNodeQuery(midZoneDbRef));
		
		var objResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create DBNestedZonedObj"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		
		// Set midZone to be zoned to topZone
		await Database.SetObjectZone(midZone.Known, topZone.Known, CancellationToken.None);
		
		// Set obj to be zoned to midZone
		await Database.SetObjectZone(obj.Known, midZone.Known, CancellationToken.None);
		
		// Verify the chain
		var updatedMid = await Mediator.Send(new GetObjectNodeQuery(midZoneDbRef));
		var midZoneZone = await updatedMid.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(midZoneZone.Known.Object().DBRef.Number).IsEqualTo(topZoneDbRef.Number);
		
		var updatedObj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var objZone = await updatedObj.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(objZone.Known.Object().DBRef.Number).IsEqualTo(midZoneDbRef.Number);
	}
}
