using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

public class ZoneDatabaseTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async ValueTask SetObjectZone()
	{
		var zoneResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBZoneMaster");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = (await Mediator.Send(new GetObjectNodeQuery(zoneDbRef))).Expect<AnySharpObject>();

		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBZonedObject");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectZoneCommand(zonedObject, zoneObject));

		var updatedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone = (await updatedObject.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();

		await Assert.That(zone.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(SetObjectZone))]
	public async ValueTask UnsetObjectZone()
	{
		var zoneResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBUnsetZoneMaster");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = (await Mediator.Send(new GetObjectNodeQuery(zoneDbRef))).Expect<AnySharpObject>();

		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBUnsetZonedObject");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectZoneCommand(zonedObject, zoneObject));

		var withZone = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zoneCheck = await withZone.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zoneCheck.IsNone).IsFalse();

		await Mediator.Send(new UnsetObjectZoneCommand(withZone));

		var updatedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone = await updatedObject.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(zone.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(UnsetObjectZone))]
	public async ValueTask UpdateObjectZone()
	{
		var zone1Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBUpdateZone1");
		var zone1DbRef = DBRef.Parse(zone1Result.Message!.ToPlainText()!);
		var zone1Object = (await Mediator.Send(new GetObjectNodeQuery(zone1DbRef))).Expect<AnySharpObject>();

		var zone2Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBUpdateZone2");
		var zone2DbRef = DBRef.Parse(zone2Result.Message!.ToPlainText()!);
		var zone2Object = (await Mediator.Send(new GetObjectNodeQuery(zone2DbRef))).Expect<AnySharpObject>();

		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBUpdateZonedObject");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectZoneCommand(zonedObject, zone1Object));

		var withZone1 = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone1Check = (await withZone1.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(zone1Check.Object().DBRef.Number).IsEqualTo(zone1DbRef.Number);

		await Mediator.Send(new SetObjectZoneCommand(withZone1, zone2Object));

		var withZone2 = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone2Check = (await withZone2.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(zone2Check.Object().DBRef.Number).IsEqualTo(zone2DbRef.Number);
	}

	[Test]
	[DependsOn(nameof(UpdateObjectZone))]
	public async ValueTask SetObjectZoneToNull()
	{
		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBNullZoneObject");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		// This bypasses the Mediator and tests the database implementation
		await Database.SetObjectZone(zonedObject, null, CancellationToken.None);

		var updatedObject = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var zone = await updatedObject.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(zone.IsNone).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SetObjectZoneToNull))]
	public async ValueTask MultipleObjectsSameZone()
	{
		var zoneResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBSharedZone");
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = (await Mediator.Send(new GetObjectNodeQuery(zoneDbRef))).Expect<AnySharpObject>();

		var obj1Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBSharedZoneObj1");
		var obj1DbRef = DBRef.Parse(obj1Result.Message!.ToPlainText()!);
		var obj1 = (await Mediator.Send(new GetObjectNodeQuery(obj1DbRef))).Expect<AnySharpObject>();

		var obj2Result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBSharedZoneObj2");
		var obj2DbRef = DBRef.Parse(obj2Result.Message!.ToPlainText()!);
		var obj2 = (await Mediator.Send(new GetObjectNodeQuery(obj2DbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectZoneCommand(obj1, zoneObject));
		await Mediator.Send(new SetObjectZoneCommand(obj2, zoneObject));

		var updated1 = (await Mediator.Send(new GetObjectNodeQuery(obj1DbRef))).Expect<AnySharpObject>();
		var zone1 = (await updated1.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();

		var updated2 = (await Mediator.Send(new GetObjectNodeQuery(obj2DbRef))).Expect<AnySharpObject>();
		var zone2 = (await updated2.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();

		await Assert.That(zone1.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
		await Assert.That(zone2.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	[DependsOn(nameof(MultipleObjectsSameZone))]
	public async ValueTask ObjectCanBeZone()
	{
		// An object can be both a zone master and be zoned to another zone
		var topZoneResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBTopZone");
		var topZoneDbRef = DBRef.Parse(topZoneResult.Message!.ToPlainText()!);
		var topZone = (await Mediator.Send(new GetObjectNodeQuery(topZoneDbRef))).Expect<AnySharpObject>();

		var midZoneResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBMidZone");
		var midZoneDbRef = DBRef.Parse(midZoneResult.Message!.ToPlainText()!);
		var midZone = (await Mediator.Send(new GetObjectNodeQuery(midZoneDbRef))).Expect<AnySharpObject>();

		var objResult = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, "DBNestedZonedObj");
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();

		await Mediator.Send(new SetObjectZoneCommand(midZone, topZone));

		await Mediator.Send(new SetObjectZoneCommand(obj, midZone));

		var updatedMid = (await Mediator.Send(new GetObjectNodeQuery(midZoneDbRef))).Expect<AnySharpObject>();
		var midZoneZone = (await updatedMid.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(midZoneZone.Object().DBRef.Number).IsEqualTo(topZoneDbRef.Number);

		var updatedObj = (await Mediator.Send(new GetObjectNodeQuery(objDbRef))).Expect<AnySharpObject>();
		var objZone = (await updatedObj.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>();
		await Assert.That(objZone.Object().DBRef.Number).IsEqualTo(midZoneDbRef.Number);
	}
}
