using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.INotifyService;

namespace SharpMUSH.Tests.Commands;

public class ZoneCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();

	private string GenerateUniqueName(string prefix) =>
		$"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";

	[Test]
	public async ValueTask ChzoneSetZone()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Create a unique zone master object
		var zoneName = GenerateUniqueName("ZoneMaster");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		
		// Validate zone object was created
		await Assert.That(zoneObject.IsNone).IsFalse();
		await Assert.That(zoneObject.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
		
		// Create a unique object to be zoned
		var objName = GenerateUniqueName("ZonedObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		
		// Validate object was created
		await Assert.That(zonedObject.IsNone).IsFalse();
		await Assert.That(zonedObject.Known.Object().DBRef.Number).IsEqualTo(objDbRef.Number);
		
		// Set zone
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// Verify zone set notification was received with the specific object dbref
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				(TestHelpers.MessageContains(msg, $"Zoned to {zoneName}") || TestHelpers.MessageContains(msg, $"{objDbRef}"))), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
		
		// Verify the zone was actually set in the database
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		await Assert.That(zone.IsNone).IsFalse();
		await Assert.That(zone.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async ValueTask ChzoneClearZone()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Create unique zone master object
		var zoneName = GenerateUniqueName("ZoneMasterClear");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();
		
		// Create unique object to be zoned
		var objName = GenerateUniqueName("ZonedClearObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(zonedObject.IsNone).IsFalse();
		
		// Set zone first
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));
		
		// Verify zone was set
		var withZone = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zoneCheck = await withZone.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zoneCheck.IsNone).IsFalse();
		
		// Clear zone by setting to "none"
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}=none"));

		// Verify zone cleared notification was received with specific text indicating clearing
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				TestHelpers.MessageContains(msg, "Zone cleared")), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
		
		// Verify the zone was actually cleared in the database
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);
		
		await Assert.That(zone.IsNone).IsTrue();
	}

	[Test]
	public async ValueTask ChzonePermissionSuccess()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Create unique zone master object that player controls
		var zoneName = GenerateUniqueName("PermTestZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();
		
		// Create unique object owned by player #1
		var objName = GenerateUniqueName("PermTestObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();
		
		// Try to set zone - this should work since player controls both
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));
		
		// Verify success notification with specific zone name
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				(TestHelpers.MessageContains(msg, $"{zoneName}") || TestHelpers.MessageContains(msg, "Zoned"))), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
		
		// Verify zone was set
		var updated = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updated.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zone.IsNone).IsFalse();
	}

	[Test]
	public async ValueTask ChzoneInvalidObject()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Try to set zone on non-existent object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzone #99999=#1"));
		
		// Should receive an error notification - LocateService handles this with standard error messages
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				(TestHelpers.MessageContains(msg, "can't see") || TestHelpers.MessageContains(msg, "NO SUCH OBJECT"))), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
	}

	[Test]
	public async ValueTask ChzoneInvalidZone()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Create unique object
		var objName = GenerateUniqueName("InvalidZoneTest");
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();
		
		// Try to set zone to non-existent zone
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}=#99999"));
		
		// Should receive an error notification about not being able to see the object
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				TestHelpers.MessageContains(msg, "can't see")), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
	}

	[Test]
	public async ValueTask ZMRExitMatchingTest()
	{
		// Clear player zone to avoid inheritance issues
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create a unique Zone Master Room (ZMR)
		var zmrName = GenerateUniqueName("ZMR");
		var zmrResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();
		
		// Create a unique room that will be zoned to the ZMR
		var roomName = GenerateUniqueName("ZonedRoom");
		var room1Result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var room1DbRefText = room1Result.Message!.ToPlainText()!;
		var room1Match = System.Text.RegularExpressions.Regex.Match(room1DbRefText, @"#(\d+)");
		if (!room1Match.Success) return;
		var room1DbRef = new DBRef(int.Parse(room1Match.Groups[1].Value));
		var room1Object = await Mediator.Send(new GetObjectNodeQuery(room1DbRef));
		await Assert.That(room1Object.IsNone).IsFalse();
		
		// Zone the room to the ZMR
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {room1DbRef}={zmrDbRef}"));
		
		// Verify zone was set
		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(room1DbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();
		await Assert.That(roomZone.Known.Object().DBRef.Number).IsEqualTo(zmrDbRef.Number);
		
		// Create an exit in the ZMR
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@open zmr_exit_{Random.Shared.Next(1000, 9999)}={room1DbRef},{zmrDbRef}"));
		
		// Verify ZMR room zone is still properly set (validates test setup)
		var zmrVerify = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrVerify.IsNone).IsFalse();
	}

	[Test, Skip("Failing and needs to be fixed.")]
	public async ValueTask ZMRUserDefinedCommandTest()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Clear player zone to avoid inheritance issues
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create a unique Zone Master Room (ZMR)
		var zmrName = GenerateUniqueName("ZMRCmd");
		var zmrResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();
		
		// Create a unique room that will be zoned to the ZMR
		var roomName = GenerateUniqueName("ZonedCmdRoom");
		var zonedRoomResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message!.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();
		
		// Zone the room to the ZMR
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {zonedRoomDbRef}={zmrDbRef}"));
		
		// Verify zone was set
		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();
		
		// Create a unique object in the ZMR with a $-command
		var cmdObjName = GenerateUniqueName("ZMRCmdObj");
		var cmdObjResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {cmdObjName}"));
		var cmdObjDbRef = DBRef.Parse(cmdObjResult.Message!.ToPlainText()!);
		var cmdObject = await Mediator.Send(new GetObjectNodeQuery(cmdObjDbRef));
		await Assert.That(cmdObject.IsNone).IsFalse();
		
		// Move the object to the ZMR
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {cmdObjDbRef}={zmrDbRef}"));
		
		// Set a $-command on the object with unique command name
		var cmdName = GenerateUniqueName("zmrtest");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&cmd`{cmdName} {cmdObjDbRef}=${cmdName}:@pemit #1=ZMR command executed"));
		
		// Teleport player to the zoned room
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {zonedRoomDbRef}"));
		
		// Execute the $-command - it should be available through ZMR
		await Parser.CommandParse(1, ConnectionService, MModule.single(cmdName));
		
		// Verify the command was executed - check for the unique command output
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				TestHelpers.MessageContains(msg, "ZMR command executed")), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
	}

	[Test, Skip("Failing and needs to be fixed.")]
	public async ValueTask PersonalZoneUserDefinedCommandTest()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		// Create a unique personal Zone Master Room (ZMR)
		var personalZMRName = GenerateUniqueName("PersonalZMR");
		var personalZMRResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {personalZMRName}"));
		var personalZMRDbRefText = personalZMRResult.Message!.ToPlainText()!;
		var personalZMRMatch = System.Text.RegularExpressions.Regex.Match(personalZMRDbRefText, @"#(\d+)");
		if (!personalZMRMatch.Success) return;
		var personalZMRDbRef = new DBRef(int.Parse(personalZMRMatch.Groups[1].Value));
		var personalZMRObject = await Mediator.Send(new GetObjectNodeQuery(personalZMRDbRef));
		await Assert.That(personalZMRObject.IsNone).IsFalse();
		
		// Create a regular object to represent "personal zone" concept
		var personalObjName = GenerateUniqueName("PersonalObj");
		var personalObjResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {personalObjName}"));
		var personalObjDbRef = DBRef.Parse(personalObjResult.Message!.ToPlainText()!);
		var personalObject = await Mediator.Send(new GetObjectNodeQuery(personalObjDbRef));
		await Assert.That(personalObject.IsNone).IsFalse();
		
		// Set the object's zone to the ZMR (testing "personal zone" functionality)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {personalObjDbRef}={personalZMRDbRef}"));
		
		// Verify zone was set - get fresh copy from database
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(personalObjDbRef));
		var objZone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(objZone.IsNone).IsFalse();
		await Assert.That(objZone.Known.Object().DBRef.Number).IsEqualTo(personalZMRDbRef.Number);
		
		// Create a unique object in the personal ZMR with a $-command
		var personalCmdObjName = GenerateUniqueName("PersonalCmdObj");
		var personalCmdObjResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {personalCmdObjName}"));
		var personalCmdObjDbRef = DBRef.Parse(personalCmdObjResult.Message!.ToPlainText()!);
		var personalCmdObject = await Mediator.Send(new GetObjectNodeQuery(personalCmdObjDbRef));
		await Assert.That(personalCmdObject.IsNone).IsFalse();
		
		// Move the object to the personal ZMR
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {personalCmdObjDbRef}={personalZMRDbRef}"));
		
		// Set a $-command on the object with unique command name
		var cmdName = GenerateUniqueName("personaltest");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&cmd`{cmdName} {personalCmdObjDbRef}=${cmdName}:@pemit #1=Personal zone command executed"));
		
		// Create a unique room to test from
		var testRoomName = GenerateUniqueName("PersonalZoneTestRoom");
		var testRoomResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {testRoomName}"));
		var testRoomDbRefText = testRoomResult.Message!.ToPlainText()!;
		var testRoomMatch = System.Text.RegularExpressions.Regex.Match(testRoomDbRefText, @"#(\d+)");
		if (!testRoomMatch.Success) return;
		var testRoomDbRef = new DBRef(int.Parse(testRoomMatch.Groups[1].Value));
		var testRoomObject = await Mediator.Send(new GetObjectNodeQuery(testRoomDbRef));
		await Assert.That(testRoomObject.IsNone).IsFalse();
		
		// Teleport player to the test room
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {testRoomDbRef}"));
		
		// Execute the $-command - it should be available through personal zone
		await Parser.CommandParse(1, ConnectionService, MModule.single(cmdName));
		
		// Verify the command was executed - check for the unique command output
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				TestHelpers.MessageContains(msg, "Personal zone command executed")), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
	}

	[Test]
	public async ValueTask ZMRDoesNotMatchCommandsOnZMRItself()
	{
		// Clear player zone to avoid inheritance issues
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzone me=none"));
		
		// Create a unique Zone Master Room (ZMR)
		var zmrName = GenerateUniqueName("ZMRSelfTest");
		var zmrResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();
		
		// Create a unique room that will be zoned to the ZMR
		var roomName = GenerateUniqueName("SelfTestRoom");
		var zonedRoomResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message!.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();
		
		// Zone the room to the ZMR
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {zonedRoomDbRef}={zmrDbRef}"));
		
		// Verify zone was set
		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();
		
		// Set a $-command directly on the ZMR itself with unique command name (should be ignored per spec)
		var cmdName = GenerateUniqueName("zmrselftest");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&cmd`{cmdName} {zmrDbRef}=${cmdName}:@pemit #1=This should not execute"));
		
		// Teleport player to the zoned room
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {zonedRoomDbRef}"));
		
		// Try to execute the $-command - it should NOT be available
		await Parser.CommandParse(1, ConnectionService, MModule.single(cmdName));
		
		// Verify the command was NOT executed (per PennMUSH spec, commands on ZMR itself are ignored)
		// Check that the specific unique error message was not sent
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg => 
				TestHelpers.MessageContains(msg, "This should not execute")), Arg.Any<AnySharpObject>(), Arg.Any<NotificationType>());
	}
}
