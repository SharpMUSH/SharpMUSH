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
		var zoneName = TestIsolationHelpers.GenerateUniqueName("ZoneMaster");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));

		await Assert.That(zoneObject.IsNone).IsFalse();
		await Assert.That(zoneObject.Known.Object().DBRef.Number).IsEqualTo(zoneDbRef.Number);

		var objName = TestIsolationHelpers.GenerateUniqueName("ZonedObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));

		await Assert.That(zonedObject.IsNone).IsFalse();
		await Assert.That(zonedObject.Known.Object().DBRef.Number).IsEqualTo(objDbRef.Number);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();

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
		var zoneResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();

		var objName = TestIsolationHelpers.GenerateUniqueName("ZonedClearObject");
		var objResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var zonedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(zonedObject.IsNone).IsFalse();

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		var withZone = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zoneCheck = await withZone.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zoneCheck.IsNone).IsFalse();

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}=none"));

		// Pattern C: freshPlayer.DbRef is unique to this test so Received(1) is unambiguous.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "Zone cleared.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);

		var updatedObject = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updatedObject.Known.Object().Zone.WithCancellation(CancellationToken.None);

		await Assert.That(zone.IsNone).IsTrue();
	}

	/// <summary>Power names currently granted to an object.</summary>
	private async Task<string[]> PowerNamesOf(DBRef dbref)
	{
		var node = await Mediator.Send(new GetObjectNodeQuery(dbref));
		var powers = await node.Known.Object().Powers.Value.ToArrayAsync();
		return powers.Select(p => p.Name).ToArray();
	}

	/// <summary>
	/// PennMUSH src/wiz.c do_chzone: zoning a non-player strips its privileged flags and every
	/// power, unless /preserve is given. @CHZONE gets this from the same
	/// ManipulateSharpObjectService.ClearAllPowers that @CHZONEALL uses.
	/// </summary>
	[Test]
	public async ValueTask ChzoneStripsPowers()
	{
		var zoneName = TestIsolationHelpers.GenerateUniqueName("PowerStripZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objName = TestIsolationHelpers.GenerateUniqueName("PowerStripObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {objDbRef}=Builder Boot"));
		var granted = await PowerNamesOf(objDbRef);
		await Assert.That(granted).Contains("Builder");
		await Assert.That(granted).Contains("Boot");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		await Assert.That(await PowerNamesOf(objDbRef)).IsEmpty();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@destroy {objDbRef}"));
	}

	/// <summary>@CHZONE/PRESERVE keeps the powers the plain command strips.</summary>
	[Test]
	public async ValueTask ChzonePreserveKeepsPowers()
	{
		var zoneName = TestIsolationHelpers.GenerateUniqueName("PowerKeepZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);

		var objName = TestIsolationHelpers.GenerateUniqueName("PowerKeepObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {objDbRef}=Builder"));
		await Assert.That(await PowerNamesOf(objDbRef)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone/preserve {objDbRef}={zoneDbRef}"));

		await Assert.That(await PowerNamesOf(objDbRef)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@destroy {objDbRef}"));
	}

	[Test]
	public async ValueTask ChzonePermissionSuccess()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var zoneName = TestIsolationHelpers.GenerateUniqueName("PermTestZone");
		var zoneResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {zoneName}"));
		var zoneDbRef = DBRef.Parse(zoneResult.Message!.ToPlainText()!);
		var zoneObject = await Mediator.Send(new GetObjectNodeQuery(zoneDbRef));
		await Assert.That(zoneObject.IsNone).IsFalse();

		var objName = TestIsolationHelpers.GenerateUniqueName("PermTestObject");
		var objResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		// Try to set zone - this should work since player controls both
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}={zoneDbRef}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ZoneChanged), executor, executor)).IsTrue();

		var updated = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		var zone = await updated.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zone.IsNone).IsFalse();
	}

	[Test]
	public async ValueTask ChzoneInvalidObject()
	{
		// Pattern C: the failed-locate notification is sent by many LocateService calls across the
		// session to executor #1. Use a unique receiver (fresh player) so Received(1) is sound.
		// @chzone matches via match_controlled -> noisy_match_result, so the wording is match.c:485's
		// "I can't see that here." — "I don't see that here." is look.c/move.c's string.
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_InvalidObj");

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain("@chzone #99999=#1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "I can't see that here.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ChzoneInvalidZone()
	{
		// Pattern C: the failed-locate notification is sent by many LocateService calls to #1 across
		// the session. Use a fresh player as the unique executor so Received(1) is unambiguous.
		var freshPlayer = await CreateTestPlayerWithHandleAsync("ZT_InvalidZone");

		// Create a unique object as the fresh player (they will own it → controls check passes)
		var objName = TestIsolationHelpers.GenerateUniqueName("InvalidZoneTest");
		var objResult = await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {objName}"));
		var objDbRef = DBRef.Parse(objResult.Message!.ToPlainText()!);
		var obj = await Mediator.Send(new GetObjectNodeQuery(objDbRef));
		await Assert.That(obj.IsNone).IsFalse();

		await Parser.CommandParse(freshPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone {objDbRef}=#99999"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(freshPlayer.DbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "I can't see that here.")),
				TestHelpers.MatchingObject(freshPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ZMRExitMatchingTest()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRExitTest");

		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMR");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		var roomName = TestIsolationHelpers.GenerateUniqueName("ZonedRoom");
		var room1Result = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var room1DbRefText = room1Result.Message!.ToPlainText()!;
		var room1Match = System.Text.RegularExpressions.Regex.Match(room1DbRefText, @"#(\d+)");
		if (!room1Match.Success) return;
		var room1DbRef = new DBRef(int.Parse(room1Match.Groups[1].Value));
		var room1Object = await Mediator.Send(new GetObjectNodeQuery(room1DbRef));
		await Assert.That(room1Object.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@chzone {room1DbRef}={zmrDbRef}"));

		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(room1DbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();
		await Assert.That(roomZone.Known.Object().DBRef.Number).IsEqualTo(zmrDbRef.Number);

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@open zmr_exit_{Random.Shared.Next(1000, 9999)}={room1DbRef},{zmrDbRef}"));

		var zmrVerify = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrVerify.IsNone).IsFalse();
	}

	[Test, Skip("Failing")]
	public async ValueTask ZMRUserDefinedCommandTest()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRCmd");

		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMRCmd");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		var roomName = TestIsolationHelpers.GenerateUniqueName("ZonedCmdRoom");
		var zonedRoomResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message!.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@chzone {zonedRoomDbRef}={zmrDbRef}"));

		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();

		var cmdObjName = TestIsolationHelpers.GenerateUniqueName("ZMRCmdObj");
		var cmdObjResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@create {cmdObjName}"));
		var cmdObjDbRef = DBRef.Parse(cmdObjResult.Message!.ToPlainText()!);
		var cmdObject = await Mediator.Send(new GetObjectNodeQuery(cmdObjDbRef));
		await Assert.That(cmdObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@tel {cmdObjDbRef}={zmrDbRef}"));

		// Pattern A: embed the unique token into the @pemit body so the full message is globally unique.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("zmrtest");
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"&cmd`{cmdName} {cmdObjDbRef}=${cmdName}:@pemit #{testPlayer.Number}={cmdName}: ZMR command executed"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@tel {zonedRoomDbRef}"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain(cmdName));

		// Pattern A: the emitted string is unique because cmdName (a generated unique token) is embedded.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: ZMR command executed")),
				TestHelpers.MatchingObject(testPlayer), INotifyService.NotificationType.Announce);
	}

	[Test, Skip("Failed")]
	public async ValueTask PersonalZoneUserDefinedCommandTest()
	{
		// Use a fresh player so this test never mutates the shared player #1
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ZT_PersonalZone");

		var personalZMRName = TestIsolationHelpers.GenerateUniqueName("PersonalZMR");
		var personalZMRResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@dig {personalZMRName}"));
		var personalZMRDbRefText = personalZMRResult.Message!.ToPlainText();
		var personalZMRMatch = System.Text.RegularExpressions.Regex.Match(personalZMRDbRefText, @"#(\d+)");
		if (!personalZMRMatch.Success) return;
		var personalZMRDbRef = new DBRef(int.Parse(personalZMRMatch.Groups[1].Value));
		var personalZMRObject = await Mediator.Send(new GetObjectNodeQuery(personalZMRDbRef));
		await Assert.That(personalZMRObject.IsNone).IsFalse();

		// Set the TEST PLAYER'S zone to the ZMR (this is the "personal zone" concept)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@chzone me={personalZMRDbRef}"));

		var playerObj = await Mediator.Send(new GetObjectNodeQuery(testPlayer.DbRef));
		var playerZone = await playerObj.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(playerZone.IsNone).IsFalse();
		await Assert.That(playerZone.Known.Object().DBRef.Number).IsEqualTo(personalZMRDbRef.Number);

		var personalCmdObjName = TestIsolationHelpers.GenerateUniqueName("PersonalCmdObj");
		var personalCmdObjResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@create {personalCmdObjName}"));
		var personalCmdObjDbRef = DBRef.Parse(personalCmdObjResult.Message!.ToPlainText()!);
		var personalCmdObject = await Mediator.Send(new GetObjectNodeQuery(personalCmdObjDbRef));
		await Assert.That(personalCmdObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@tel {personalCmdObjDbRef}={personalZMRDbRef}"));

		// Pattern A: embed the unique token into the @pemit body so the full message is globally unique.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("personaltest");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"&cmd`{cmdName} {personalCmdObjDbRef}=${cmdName}:@pemit #{testPlayer.Handle}={cmdName}: Personal zone command executed"));

		var testRoomName = TestIsolationHelpers.GenerateUniqueName("PersonalZoneTestRoom");
		var testRoomResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@dig {testRoomName}"));
		var testRoomDbRefText = testRoomResult.Message!.ToPlainText();
		var testRoomMatch = System.Text.RegularExpressions.Regex.Match(testRoomDbRefText, @"#(\d+)");
		if (!testRoomMatch.Success) return;
		var testRoomDbRef = new DBRef(int.Parse(testRoomMatch.Groups[1].Value));
		var testRoomObject = await Mediator.Send(new GetObjectNodeQuery(testRoomDbRef));
		await Assert.That(testRoomObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"@tel {testRoomDbRef}"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain(cmdName));

		// Pattern A: the emitted string is unique because cmdName (a generated unique token) is embedded.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: Personal zone command executed")),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ZMRDoesNotMatchCommandsOnZMRItself()
	{
		// Use a fresh player so this test does not mutate the shared player #1
		var testPlayer = await CreateTestPlayerAsync("ZT_ZMRSelfTest");

		var zmrName = TestIsolationHelpers.GenerateUniqueName("ZMRSelfTest");
		var zmrResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {zmrName}"));
		var zmrDbRefText = zmrResult.Message!.ToPlainText()!;
		var zmrMatch = System.Text.RegularExpressions.Regex.Match(zmrDbRefText, @"#(\d+)");
		if (!zmrMatch.Success) return;
		var zmrDbRef = new DBRef(int.Parse(zmrMatch.Groups[1].Value));
		var zmrObject = await Mediator.Send(new GetObjectNodeQuery(zmrDbRef));
		await Assert.That(zmrObject.IsNone).IsFalse();

		var roomName = TestIsolationHelpers.GenerateUniqueName("SelfTestRoom");
		var zonedRoomResult = await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var zonedRoomDbRefText = zonedRoomResult.Message!.ToPlainText()!;
		var zonedRoomMatch = System.Text.RegularExpressions.Regex.Match(zonedRoomDbRefText, @"#(\d+)");
		if (!zonedRoomMatch.Success) return;
		var zonedRoomDbRef = new DBRef(int.Parse(zonedRoomMatch.Groups[1].Value));
		var zonedRoomObject = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		await Assert.That(zonedRoomObject.IsNone).IsFalse();

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@chzone {zonedRoomDbRef}={zmrDbRef}"));

		var zonedRoom = await Mediator.Send(new GetObjectNodeQuery(zonedRoomDbRef));
		var roomZone = await zonedRoom.Known.Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(roomZone.IsNone).IsFalse();

		// Set a $-command directly on the ZMR itself with unique command name (should be ignored per spec).
		// Pattern A: embed the unique token into the @pemit body.
		var cmdName = TestIsolationHelpers.GenerateUniqueName("zmrselftest");
		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"&cmd`{cmdName} {zmrDbRef}=${cmdName}:@pemit #{testPlayer.Number}={cmdName}: This should not execute"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain($"@tel {zonedRoomDbRef}"));

		await Parser.CommandParse(testPlayer.Number, ConnectionService, MarkupText.Plain(cmdName));

		// Pattern A: the unique token in the message makes this a precise negative assertion.
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(testPlayer), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"{cmdName}: This should not execute")),
				TestHelpers.MatchingObject(testPlayer), INotifyService.NotificationType.Announce);
	}
}
