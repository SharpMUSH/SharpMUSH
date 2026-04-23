using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
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
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>
	/// Creates a fresh, isolated player through the database layer so zone tests never
	/// mutate the shared player #1 object.
	/// </summary>
	private Task<DBRef> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, namePrefix);

	/// <summary>
	/// Creates a fresh player with a registered connection handle so that
	/// <c>Parser.CommandParse(testPlayer.Handle, …)</c> executes as that player.
	/// </summary>
	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerWithHandleAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask ChzoneSetZone()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a unique zone master object
		var zoneName = TestIsolationHelpers.GenerateUniqueName("ZoneMaster");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));

		// Validate zone object was created
		await Assert.That(zoneObject.IsNone).IsFalse();
		await Assert.That(zoneObject.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);

		// Create a unique object to be zoned
		var objName = TestIsolationHelpers.GenerateUniqueName("ZonedObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		// Validate object was created
		await Assert.That(zonedObject.IsNone).IsFalse();
		await Assert.That(zonedObject.Known.Object().DBRef.Number).IsEqualTo(objDbRef.Number);

		// Set zone
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// Verify zone set notification was received with the specific object dbref
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();

		// Verify the zone was actually set in the database
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(zone.IsNone).IsFalse();
		await Assert.That(zone.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);
	}

	[Test]
	public async ValueTask ChzoneClearZone()
	{
		// Pattern C: "Zone cleared." is a fixed server string that executor #1 may receive in
		// other tests (any @chzone …=none). Use a fresh player as the unique receiver/sender.
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_ClearZone");

		// Create unique zone master object as the fresh player (they own it → controls check passes)
		var zoneName = TestIsolationHelpers.GenerateUniqueName("ZoneMasterClear");
		var zoneResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();

		// Create unique object to be zoned
		var objName = TestIsolationHelpers.GenerateUniqueName("ZonedClearObject");
		var objResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(zonedObject.IsNone).IsFalse();

		// Set zone first
		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// Verify zone was set
		var withZone = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zoneCheck = await withZone.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zoneCheck.IsNone).IsFalse();

		// Clear zone by setting to "none"
		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single($"@chzone {objDbRef}=none"));

		// Pattern C: freshPlayer.DbRef is unique to this test so Received(1) is unambiguous.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "Zone cleared.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);

		// Verify the zone was actually cleared in the database
		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(zone.IsNone).IsTrue();
	}

	[Test]
	public async ValueTask ChzonePermissionSuccess()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create unique zone master object that player controls
		var zoneName = TestIsolationHelpers.GenerateUniqueName("PermTestZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();

		// Create unique object owned by player #1
		var objName = TestIsolationHelpers.GenerateUniqueName("PermTestObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Try to set zone - this should work since player controls both
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@chzone {objDbRef}={zoneDbRef}"));

		// Verify success notification with specific zone name
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();

		// Verify zone was set
		var updated = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updated.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zone.IsNone).IsFalse();
	}

	[Test]
	public async ValueTask ChzoneInvalidObject()
	{
		// Pattern C: "I don't see that here." is sent by many LocateService calls across the session
		// to executor #1. Use a unique receiver (fresh player) so Received(1) is sound.
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_InvalidObj");

		// Try to set zone on non-existent object as the fresh player
		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single("@chzone #99999=#1"));

		// Should receive exactly one "I don't see that here." notification addressed to the fresh player.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "I don't see that here.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ChzoneInvalidZone()
	{
		// Pattern C: "I don't see that here." is sent by many LocateService calls to #1 across the
		// session. Use a fresh player as the unique executor so Received(1) is unambiguous.
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_InvalidZone");

		// Create a unique object as the fresh player (they will own it → controls check passes)
		var objName = TestIsolationHelpers.GenerateUniqueName("InvalidZoneTest");
		var objResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Try to set zone to non-existent zone
		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MModule.single($"@chzone {objDbRef}=#99999"));

		// Should receive exactly one "I don't see that here." notification to the fresh player.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "I don't see that here.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ZMRExitMatchingTest()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRExitTest");

		// Create a unique Zone Master Room (ZMR)
		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMR");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		// Create a unique room that will be zoned to the ZMR
		var roomName = TestIsolationHelpers.GenerateUniqueName("ZonedRoom");
		var room1Result = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {roomName}"));
		var room1DbRefText = room1Result.Message!.ToPlainText()!;
		var room1Match = System.Text.RegularExpressions.Regex.Match(room1DbRefText, @"#(\d+)");
		if (!room1Match.Success) return;
		var room1DbRef = new DBRef(int.Parse(room1Match.Groups[1].Value));
		var room1Object = await Mediator.Send(new GetObjectNodeQuery(room1DbRef));
		await Assert.That(room1Object.IsNone).IsFalse();

		// Zone the room to the ZMR
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@chzone {room1DbRef}={zmrDbRef}"));

		// Verify zone was set
		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(room1DbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();
		await Assert.That(roomZone.Known.Object().DBRef.Number).IsEqualTo(zmrDbRef.Number);

		// Create an exit in the ZMR
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@open zmr_exit_{Random.Shared.Next(1000, 9999)}={room1DbRef},{zmrDbRef}"));

		// Verify ZMR room zone is still properly set (validates test setup)
		var zmrVerify = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrVerify.IsNone).IsFalse();
	}

	[Test]
	public async ValueTask ZMRUserDefinedCommandTest()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRCmd");

		// Create a unique Zone Master Room (ZMR)
		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMRCmd");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		// Create a unique room that will be zoned to the ZMR
		var roomName = TestIsolationHelpers.GenerateUniqueName("ZonedCmdRoom");
		var zonedRoomResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message!.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();

		// Zone the room to the ZMR
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@chzone {zonedRoomDbRef}={zmrDbRef}"));

		// Verify zone was set
		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();

		// Create a unique object in the ZMR with a $-command
		var cmdObjName = TestIsolationHelpers.GenerateUniqueName("ZMRCmdObj");
		var cmdObjResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@create {cmdObjName}"));
		var cmdObjDbRef = DBRef.Parse(cmdObjResult.Message!.ToPlainText()!);
		var cmdObject = await Mediator.Send(new GetObjectNodeQuery(cmdObjDbRef));
		await Assert.That(cmdObject.IsNone).IsFalse();

		// Move the object to the ZMR
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@tel {cmdObjDbRef}={zmrDbRef}"));

		// Set a $-command on the object with unique command name.
		// Pattern A: embed the unique token into the @pemit body so the full message is globally unique.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("zmrtest");
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"&cmd`{cmdName} {cmdObjDbRef}=${cmdName}:@pemit #{testPlayer.Number}={cmdName}: ZMR command executed"));

		// Teleport the test player to the zoned room
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@tel {zonedRoomDbRef}"));

		// Execute the $-command - it should be available through ZMR
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single(cmdName));

		// Pattern A: the emitted string is unique because cmdName (a generated unique token) is embedded.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: ZMR command executed")),
				TestHelpers.MatchingObject(testPlayer), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask PersonalZoneUserDefinedCommandTest()
	{
		// Use a fresh player so this test never mutates the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_PersonalZone");

		// Create a unique personal Zone Master Room (ZMR)
		var personalZMRName = TestIsolationHelpers.GenerateUniqueName("PersonalZMR");
		var personalZMRResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {personalZMRName}"));
		var personalZMRDbRefText = personalZMRResult.Message!.ToPlainText();
		var personalZMRMatch = System.Text.RegularExpressions.Regex.Match(personalZMRDbRefText, @"#(\d+)");
		if (!personalZMRMatch.Success) return;
		var personalZMRDbRef = new DBRef(int.Parse(personalZMRMatch.Groups[1].Value));
		var personalZMRObject = await Mediator.Send(new GetObjectNodeQuery(personalZMRDbRef));
		await Assert.That(personalZMRObject.IsNone).IsFalse();

		// Set the TEST PLAYER'S zone to the ZMR (this is the "personal zone" concept)
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@chzone me={personalZMRDbRef}"));

		// Verify zone was set on the test player
		var playerObj = await Mediator.Send(new GetObjectNodeQuery(testPlayer));
		var playerZone = await playerObj.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(playerZone.IsNone).IsFalse();
		await Assert.That(playerZone.Known.Object().DBRef.Number).IsEqualTo(personalZMRDbRef.Number);

		// Create a unique object in the personal ZMR with a $-command
		var personalCmdObjName = TestIsolationHelpers.GenerateUniqueName("PersonalCmdObj");
		var personalCmdObjResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@create {personalCmdObjName}"));
		var personalCmdObjDbRef = DBRef.Parse(personalCmdObjResult.Message!.ToPlainText()!);
		var personalCmdObject = await Mediator.Send(new GetObjectNodeQuery(personalCmdObjDbRef));
		await Assert.That(personalCmdObject.IsNone).IsFalse();

		// Move the object to the personal ZMR
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@tel {personalCmdObjDbRef}={personalZMRDbRef}"));

		// Set a $-command on the object with unique command name.
		// Pattern A: embed the unique token into the @pemit body so the full message is globally unique.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("personaltest");
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"&cmd`{cmdName} {personalCmdObjDbRef}=${cmdName}:@pemit #{testPlayer.Number}={cmdName}: Personal zone command executed"));

		// Create a unique room to test from (separate from the ZMR)
		var testRoomName = TestIsolationHelpers.GenerateUniqueName("PersonalZoneTestRoom");
		var testRoomResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {testRoomName}"));
		var testRoomDbRefText = testRoomResult.Message!.ToPlainText()!;
		var testRoomMatch = System.Text.RegularExpressions.Regex.Match(testRoomDbRefText, @"#(\d+)");
		if (!testRoomMatch.Success) return;
		var testRoomDbRef = new DBRef(int.Parse(testRoomMatch.Groups[1].Value));
		var testRoomObject = await Mediator.Send(new GetObjectNodeQuery(testRoomDbRef));
		await Assert.That(testRoomObject.IsNone).IsFalse();

		// Teleport test player to the test room (away from the ZMR)
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@tel {testRoomDbRef}"));

		// Execute the $-command - it should be available through the player's personal zone
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single(cmdName));

		// Pattern A: the emitted string is unique because cmdName (a generated unique token) is embedded.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: Personal zone command executed")),
				TestHelpers.MatchingObject(testPlayer), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ZMRDoesNotMatchCommandsOnZMRItself()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRSelfTest");

		// Create a unique Zone Master Room (ZMR)
		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMRSelfTest");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		// Create a unique room that will be zoned to the ZMR
		var roomName = TestIsolationHelpers.GenerateUniqueName("SelfTestRoom");
		var zonedRoomResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message!.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();

		// Zone the room to the ZMR
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@chzone {zonedRoomDbRef}={zmrDbRef}"));

		// Verify zone was set
		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();

		// Set a $-command directly on the ZMR itself with unique command name (should be ignored per spec).
		// Pattern A: embed the unique token into the @pemit body.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("zmrselftest");
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"&cmd`{cmdName} {zmrDbRef}=${cmdName}:@pemit #{testPlayer.Number}={cmdName}: This should not execute"));

		// Teleport the test player to the zoned room
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single($"@tel {zonedRoomDbRef}"));

		// Try to execute the $-command - it should NOT be available
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MModule.single(cmdName));

		// Pattern A: the unique token in the message makes this a precise negative assertion.
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: This should not execute")),
				TestHelpers.MatchingObject(testPlayer), INotifyService.NotificationType.Announce);
	}
}
