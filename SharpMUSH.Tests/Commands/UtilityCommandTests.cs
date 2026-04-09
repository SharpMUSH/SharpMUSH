using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

using SharpMUSH.Tests;

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

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask ThinkBasic()
	{
		var testPlayer = await CreateTestPlayerAsync("ThiBas");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("think ThinkBasic Test output"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(x
				=> x.Value.ToString()!.Contains("ThinkBasic Test output")));
	}

	[Test]
	public async ValueTask ThinkWithFunction()
	{
		var testPlayer = await CreateTestPlayerAsync("ThiWitFun");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("think ThinkWithFunction [add(2,3)]"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(x
					=> x.Value.ToString()!.Contains("ThinkWithFunction 5")));
	}

	[Test]
	public async ValueTask CommentCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("ComCom");
		var executor = testPlayer.DbRef;
		var guid = Guid.NewGuid();
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@@ This is a comment {guid}"));

		// Comment should not produce any output
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(x
				=> x.Value.ToString()!.Contains($"This is a comment {guid}")));
	}

	[Test]
	public async ValueTask LookBasic()
	{
		var testPlayer = await CreateTestPlayerAsync("LooBas");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("look"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask LookAtObject()
	{
		var testPlayer = await CreateTestPlayerAsync("LooAtObj");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"look {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsNameAndDbref()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjHeaCon");
		var executor = testPlayer.DbRef;
		// Verify the name row has "Name(#dbref)" format (no space before '(') in plain text.
		// We use plain-text check because name.Hilight() inserts ANSI codes around the name.
		// Get the player's actual name from the database for assertion
		var playerObj = await Mediator.Send(new GetObjectNodeQuery(executor));
		var playerName = playerObj.Known.Object().Name;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, $"{playerName}(#{executor.Number}")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsOwnerRow()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjHeaCon");
		var executor = testPlayer.DbRef;
		// Owner row uses proper MModule composition; plain-text must contain "Owner: "
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Owner: ")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsZoneAndPowers()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjHeaCon");
		var executor = testPlayer.DbRef;
		// Zone and Powers are always shown (even when empty/nothing)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Zone: *NOTHING*")));
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Powers: ")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsWarningsChecked()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjHeaCon");
		var executor = testPlayer.DbRef;
		// "Warnings checked:" is always shown (even when empty)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Warnings checked:")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsLastModified()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjHeaCon");
		var executor = testPlayer.DbRef;
		// "Last modified:" is always shown in both examine and brief
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Last modified:")));
	}

	[Test]
	public async ValueTask ExaminePlayer_HeaderContainsQuota()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaPlaHeaCon");
		var executor = testPlayer.DbRef;
		// "Quota:" is shown for player objects (God is player #1)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Quota:")));
	}

	[Test]
	public async ValueTask ExamineRoom_ShowsExits()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaRooShoExi");
		var executor = testPlayer.DbRef;
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
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Exits:")));
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_AlsoShowsLastModified()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjBriSwi");
		var executor = testPlayer.DbRef;
		// Brief mode should also show Last modified: (it's a header field)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine/brief {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Last modified:")));
	}

	[Test]
	public async ValueTask ExamineObject_AttributeWithAnsi_PreservesMarkup()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjAttWit");
		var executor = testPlayer.DbRef;
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
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "AnsiColorText")));

		// The ANSI-rendered output must contain actual ANSI escape codes
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "AnsiColorText") &&
				msg.IsT0 && msg.AsT0.ToString().Contains("\x1b[")));
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_ShowsHeaderNotDescription()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjBriSwi");
		var executor = testPlayer.DbRef;
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single("@create BriefExamineTestObj"));
		var objDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@desc {objDbRef}=BriefShouldNotSeeThis"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"examine/brief {objDbRef}"));

		// Brief MUST show owner header (in plain text because owner name is hilighted)
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Owner: ")));

		// Brief must NOT show description text
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "BriefShouldNotSeeThis")));
	}

	[Test]
	public async ValueTask ExamineObjectOpaqueSwitch()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaObjOpaSwi");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine/opaque {testPlayer.DbRef}"));

		// /opaque should still show header
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask ExamineWithAttributePattern()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaWitAttPat");
		var executor = testPlayer.DbRef;
		// Test examining with attribute pattern (e.g., examine #1/DESC*)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {testPlayer.DbRef}/DESC*"));

		// Should display matching attributes
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask ExamineCurrentLocation()
	{
		var testPlayer = await CreateTestPlayerAsync("ExaCurLoc");
		var executor = testPlayer.DbRef;
		// Test examining with no argument (examines current location)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("examine"));

		// Should display current location
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask FindCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("FinCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@find #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask SearchCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("SeaCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@search"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EntrancesCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("EntCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@entrances #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask StatsCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("StaCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@stats"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask VersionCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("VerCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@version"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "SharpMUSH version 0")),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask ScanCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("ScaCom");
		var executor = testPlayer.DbRef;
		// Create a unique object in the executor's room and give it a $-command attribute so
		// @scan has a real match to discover and return.
		var uniqueSuffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var objectName = $"ScanTestObj_{uniqueSuffix}";
		var attrName = $"CMD_SCAN_{uniqueSuffix}";
		var commandWord = $"scantestword{uniqueSuffix.ToLowerInvariant()}";

		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@create {objectName}"));
		var createdDbref = createResult.Message?.ToPlainText() ?? string.Empty;
		await Assert.That(createdDbref).StartsWith("#").Because($"@create should return a dbref; got: '{createdDbref}'");

		// Set a $-command attribute on the object: value starts with $pattern:code
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&{attrName} {createdDbref}=${commandWord} *:think scan test triggered"));

		// @scan <commandword> test — searches the executor's location for matching $-commands
		var scanResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
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
		var testPlayer = await CreateTestPlayerAsync("DecCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@decompile {testPlayer.DbRef}"));

		// Should receive notifications for decompiled output
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Any<OneOf.OneOf<MString, string>>(),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhereisCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("WheCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@whereis {testPlayer.DbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}
}
