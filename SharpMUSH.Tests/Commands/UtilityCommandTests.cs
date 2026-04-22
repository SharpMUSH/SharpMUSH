using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class UtilityCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask ThinkBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("think ThinkBasic Test output"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(x
				=> TestHelpers.MessagePlainTextEquals(x, "ThinkBasic Test output")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ThinkWithFunction()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("think ThinkWithFunction [add(2,3)]"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(x
					=> TestHelpers.MessagePlainTextEquals(x, "ThinkWithFunction 5")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask CommentCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var guid = Guid.NewGuid();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@@ This is a comment {guid}"));

		// Comment should not produce any output
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(x
				=> TestHelpers.MessagePlainTextEquals(x, $"This is a comment {guid}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LookBasic()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookBasic");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("look"));

		// look shows the current room name with dbref and flag symbols
		// Use StartsWith because HALT flag ('h') gets set on Room Zero by other tests in the shared session
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Room Zero(#0")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LookBasic_RoomNameHasAnsiMarkup()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookBasicAnsi");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("look"));

		// The room name must be sent as an MString that, when rendered as ANSI, contains escape codes
		// because name.Hilight() applies bold+bright-white (ansi("hw", …) → ESC[1;37m).
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				msg.IsT0 &&
				TestHelpers.MessagePlainTextStartsWith(msg, "Room Zero(#0") &&
				msg.AsT0.Render("ansi").Contains("\x1b[")),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LookAtObject()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookAtObj");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("look #1"));

		// looking at player #1 (God) shows the player name with dbref and flag symbols
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "God(#1")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsNameAndDbref()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamNameDbref");
		// Grant wizard so the test player can examine any object
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		// Verify the name row has "Name(#dbref)" format (no space before '(') in plain text.
		// We use plain-text check because name.Hilight() inserts ANSI codes (bold+bright-white) around the name.
		// Player #1 is named "God" in the test database.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "God(#1")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_NameRowHasAnsiMarkup()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamNameAnsi");
		// Grant wizard so the test player can examine any object
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		// The name row output must be an MString where the ANSI render contains escape codes,
		// because the object name is wrapped with Hilight() which applies bold+bright-white (ESC[1;37m).
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				msg.IsT0 &&
				TestHelpers.MessagePlainTextStartsWith(msg, "God(#1") &&
				msg.AsT0.Render("ansi").Contains("\x1b[")), 
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsOwnerRow()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Owner row uses proper MModule composition; plain-text must contain "Owner: "
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Owner: ")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsZoneAndPowers()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Zone and Powers are always shown (even when empty/nothing)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Zone: *NOTHING*")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Powers: ")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsWarningsChecked()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// "Warnings checked:" is always shown (even when empty)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Warnings checked:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsLastModified()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// "Last modified:" is always shown in both examine and brief
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Last modified:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExaminePlayer_HeaderContainsQuota()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// "Quota:" is shown for player objects (God is player #1)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Quota:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineRoom_ShowsExits()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamRoomExits");
		// Grant wizard so the test player can examine any object (room owned by player #1)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		// Dig a room with exits; the new room gets the return exit → examine should show Exits:
		var digResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("@dig ExitTestSource=North;N,South;S"));
		var digMessage = digResult?.Message?.ToPlainText();
		await Assert.That(digMessage).IsNotNull();
		var roomDbRef = DBRef.Parse(digMessage!);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"examine {roomDbRef}"));

		// The Exits: section should appear because the new room has the return exit (South;S)
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Exits:")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_AlsoShowsLastModified()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Brief mode should also show Last modified: (it's a header field)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine/brief #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Last modified:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_AttributeWithAnsi_PreservesMarkup()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamAnsiMarkup");
		// Create an object, set a DESCRIBE with ANSI color, then examine it.
		// The attribute value (an MString with markup) must survive through examine output.
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("@create AnsiExamineTestObj"));
		var objDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// [ansi(rh,...)] evaluates to an MString with red+bold markup
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@desc {objDbRef}=[ansi(rh,AnsiColorText)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"examine {objDbRef}"));

		// Plain-text content of the attribute value must appear in examine output
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "AnsiColorText")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);

		// The ANSI-rendered output must contain actual ANSI escape codes
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "AnsiColorText") &&
				msg.IsT0 && msg.AsT0.ToString().Contains("\x1b[")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_ShowsHeaderNotDescription()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@create BriefExamineTestObj"));
		var objDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@desc {objDbRef}=BriefShouldNotSeeThis"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"examine/brief {objDbRef}"));

		// Brief MUST show owner header (in plain text because owner name is hilighted)
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Owner: ")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		// Brief must NOT show description text
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "BriefShouldNotSeeThis")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineObjectOpaqueSwitch()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamOpaque");
		// Grant wizard so the test player can examine any object
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("examine/opaque #1"));

		// /opaque sends a combined multi-line output starting with "God(#1..."
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "God(#1")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineWithAttributePattern()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test examining with attribute pattern (e.g., examine #1/DESC*)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1/DESC*"));

		// Should display header with "God(#1..." followed by matching attributes
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "God(#1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ExamineCurrentLocation()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamCurLoc");
		// Grant wizard so the test player can examine room #0 (owned by player #1)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		// Test examining with no argument (examines current location)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("examine"));

		// Should display current location with "Room Zero(#0..." header
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Room Zero(#0")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask FindCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@find #0"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "***")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask SearchCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@search"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "@search: Advanced database search", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EntrancesCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@entrances #0"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Entrances to")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask StatsCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@stats"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Database Statistics:", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask VersionCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@version"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "SharpMUSH version 0")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ScanCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a unique object in the executor's room and give it a $-command attribute so
		// @scan has a real match to discover and return.
		var uniqueSuffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var objectName = $"ScanTestObj_{uniqueSuffix}";
		var attrName = $"CMD_SCAN_{uniqueSuffix}";
		var commandWord = $"scantestword{uniqueSuffix.ToLowerInvariant()}";

		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objectName}"));
		var createdDbref = createResult.Message?.ToPlainText() ?? string.Empty;
		await Assert.That(createdDbref).StartsWith("#").Because($"@create should return a dbref; got: '{createdDbref}'");

		// Set a $-command attribute on the object: value starts with $pattern:code
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&{attrName} {createdDbref}=${commandWord} *:think scan test triggered"));

		// @scan <commandword> test — searches the executor's location for matching $-commands
		var scanResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@scan {commandWord} test"));
		var scanPlainText = scanResult.Message?.ToPlainText() ?? string.Empty;

		// The return value is a space-joined list of "#{dbref.Number}/{attrName}" entries.
		// DBRef.Number is always the plain integer, even on backends that use "#{n}:{timestamp}" notation.
		var dbrefNum = DBRef.Parse(createdDbref).Number;
		await Assert.That(scanPlainText).Contains($"#{dbrefNum}/{attrName}");
	}

	[Test]
	public async ValueTask DecompileCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@decompile #1"));

		// Should receive notifications for decompiled output
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextStartsWith(msg, "@pcreate God")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhereisCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "God is in")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
