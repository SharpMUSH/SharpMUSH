using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class UtilityCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>What <paramref name="who"/> told themselves, as the commands here answer their enactor.</summary>
	private List<string> Heard(DBRef who, Func<string, bool> match) =>
	[
		.. WebAppFactoryArg.Notifications.DeliveriesFor(who)
			.Where(delivery => delivery.Sender == who
				&& delivery.Type == INotifyService.NotificationType.Announce
				&& match(delivery.Message))
			.Select(delivery => delivery.Message)
	];

	/// <summary>A wizard of the test's own, so what examine prints reaches nobody else.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> CreateWizardAsync(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=WIZARD"));
		return player;
	}

	private static string NameRow(TestIsolationHelpers.TestPlayer player) => $"{player.Name}(#{player.DbRef.Number}";

	[Test]
	public async ValueTask ThinkBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("think ThinkBasic Test output"));

		await Assert.That(Heard(executor, message => message == "ThinkBasic Test output")).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ThinkWithFunction()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("think ThinkWithFunction [add(2,3)]"));

		await Assert.That(Heard(executor, message => message == "ThinkWithFunction 5")).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask CommentCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var guid = Guid.NewGuid();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@@ This is a comment {guid}"));

		await Assert.That(Heard(executor, message => message == $"This is a comment {guid}")).IsEmpty();
	}

	[Test]
	public async ValueTask LookBasic()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookBasic");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("look"));

		// Use StartsWith because HALT flag ('h') gets set on Room Zero by other tests in the shared session
		await Assert.That(Heard(testPlayer.DbRef, message => message.StartsWith("Room Zero(#0", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask LookBasic_RoomNameHasAnsiMarkup()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookBasicAnsi");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("look"));

		// The room name must be sent as an MString that, when rendered as ANSI, contains escape codes
		// because name.Hilight() applies bold+bright-white (ansi("hw", …) → ESC[1;37m).
		await Assert.That(WebAppFactoryArg.Notifications.RawFor(testPlayer.DbRef)
				.Where(msg => TestHelpers.MessagePlainTextStartsWith(msg, "Room Zero(#0") && RendersAnsiEscapes(msg)))
			.Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask LookAtObject()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookAtObj");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("look #1"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.StartsWith("God(#1", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsNameAndDbref()
	{
		var testPlayer = await CreateWizardAsync("ExamNameDbref");
		// We use plain-text check because name.Hilight() inserts ANSI codes (bold+bright-white) around the name.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine me"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.StartsWith(NameRow(testPlayer), StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_NameRowHasAnsiMarkup()
	{
		var testPlayer = await CreateWizardAsync("ExamNameAnsi");
		// The name row output must be an MString where the ANSI render contains escape codes,
		// because the object name is wrapped with Hilight() which applies bold+bright-white (ESC[1;37m).
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine me"));

		await Assert.That(WebAppFactoryArg.Notifications.RawFor(testPlayer.DbRef)
				.Where(msg => TestHelpers.MessagePlainTextStartsWith(msg, NameRow(testPlayer)) && RendersAnsiEscapes(msg)))
			.Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsOwnerRow()
	{
		var testPlayer = await CreateWizardAsync("ExamOwnerRow");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine me"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Owner: ", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsZoneAndPowers()
	{
		var testPlayer = await CreateWizardAsync("ExamZonePowers");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine me"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Zone: *NOTHING*", StringComparison.Ordinal))).Count().IsEqualTo(1);
		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Powers: ", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsWarningsChecked()
	{
		var testPlayer = await CreateWizardAsync("ExamWarnings");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine me"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Warnings checked:", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsLastModified()
	{
		var testPlayer = await CreateWizardAsync("ExamLastMod");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine me"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Last modified:", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExaminePlayer_HeaderContainsQuota()
	{
		var testPlayer = await CreateWizardAsync("ExamQuota");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine me"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Quota:", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineRoom_ShowsExits()
	{
		var testPlayer = await CreateWizardAsync("ExamRoomExits");
		// Dig a room with exits; the new room gets the return exit → examine should show Exits:
		var digResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("@dig ExitTestSource=North;N,South;S"));
		var digMessage = digResult?.Message.ToPlainText();
		await Assert.That(digMessage).IsNotNull();
		var roomDbRef = DBRef.Parse(digMessage!);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain($"examine {roomDbRef}"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.StartsWith("Exits:", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_AlsoShowsLastModified()
	{
		var testPlayer = await CreateWizardAsync("ExamBriefMod");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine/brief me"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Last modified:", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineObject_AttributeWithAnsi_PreservesMarkup()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamAnsiMarkup");
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("@create AnsiExamineTestObj"));
		var objDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain($"@desc {objDbRef}=[ansi(rh,AnsiColorText)]"));

		var before = WebAppFactoryArg.Notifications.RawCountFor(testPlayer.DbRef);
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain($"examine {objDbRef}"));

		// Once, not twice: with ex_public_attribs on, examine prints DESCRIBE as the description and
		// examine_helper then drops it from the attribute list (look.c:310-312, :346-348).
		var carryingTheDescription = WebAppFactoryArg.Notifications.RawFor(testPlayer.DbRef)
			.Skip(before)
			.Where(msg => TestHelpers.MessagePlainTextContains(msg, "AnsiColorText"))
			.ToList();

		await Assert.That(carryingTheDescription.Count).IsEqualTo(1);
		await Assert.That(carryingTheDescription.All(RendersAnsiEscapes)).IsTrue()
			.Because("the description keeps its markup all the way to the client");
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_ShowsHeaderNotDescription()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamBriefHeader");
		var createResult = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain("@create BriefExamineTestObj"));
		var objDbRef = DBRef.Parse(createResult.Message.ToPlainText()!);
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain($"@desc {objDbRef}=BriefShouldNotSeeThis"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MarkupText.Plain($"examine/brief {objDbRef}"));

		// Brief MUST show owner header (in plain text because owner name is hilighted)
		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Owner: ", StringComparison.Ordinal))).Count().IsEqualTo(1);

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("BriefShouldNotSeeThis", StringComparison.Ordinal))).IsEmpty();
	}

	[Test]
	public async ValueTask ExamineObjectOpaqueSwitch()
	{
		var testPlayer = await CreateWizardAsync("ExamOpaque");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine/opaque me"));

		// /opaque sends a combined multi-line output starting with the name row
		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains(NameRow(testPlayer), StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineWithAttributePattern()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamPattern");
		var created = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@create ExamPatternObj"));
		var thing = DBRef.Parse(created.Message.ToPlainText());
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"&examinewithattributepattern {thing}=jim"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain($"examine {thing}/exa*"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.StartsWith("EXAMINEWITHATTRIBUTEPATTERN", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask ExamineCurrentLocation()
	{
		var testPlayer = await CreateWizardAsync("ExamCurLoc");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("examine"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains("Room Zero(#0", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	// PennMUSH src/version.c do_version prints, in order: "You are connected to <MUDNAME>", "Address:
	// <MUDURL>" *only when MUDURL is set*, "Last restarted: ...", then the version banner formatted from
	// VERSION/PATCHLEVEL/PATCHDATE — byte-identical to what src/funmisc.c fun_version returns. So
	// @version's version line and version() are one string from one source, and mud_url being unset
	// omits the Address line rather than printing a placeholder.
	[Test]
	public async ValueTask VersionCommand()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VersionCmd");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@version"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.Contains(SharpMUSH.Implementation.Generated.VersionInfo.Version, StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	[Test]
	public async ValueTask VersionCommandAgreesWithVersionFunction()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VersionAgree");
		var command = await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@version"));
		var function = await Parser.EvaluateAsync(MarkupText.Plain("version()"));

		var banner = function.ToPlainText();
		var lines = command.Message.ToPlainText().Split('\n');

		await Assert.That(banner).IsNotEmpty();
		await Assert.That(lines).Contains(banner);
		// The placeholders the two answers used to disagree over are gone for good.
		await Assert.That(command.Message.ToPlainText()).DoesNotContain("SharpMUSH version 0");
		await Assert.That(command.Message.ToPlainText()).DoesNotContain("Address: Unknown");
	}

	[Test]
	public async ValueTask ScanCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var uniqueSuffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var objectName = $"ScanTestObj_{uniqueSuffix}";
		var attrName = $"CMD_SCAN_{uniqueSuffix}";
		var commandWord = $"scantestword{uniqueSuffix.ToLowerInvariant()}";

		var createResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {objectName}"));
		var createdDbref = createResult.Message.ToPlainText();
		await Assert.That(createdDbref).StartsWith("#").Because($"@create should return a dbref; got: '{createdDbref}'");

		// Things are created NO_COMMAND, so nothing on them is scanned until the flag comes off.
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, DBRef.Parse(createdDbref));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{attrName} {createdDbref}=${commandWord} *:think scan test triggered"));

		var scanResult = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@scan {commandWord} test"));
		var scanPlainText = scanResult.Message.ToPlainText();

		// The return value is a space-joined list of "#{dbref.Number}/{attrName}" entries.
		// DBRef.Number is always the plain integer, even on backends that use "#{n}:{timestamp}" notation.
		var dbrefNum = DBRef.Parse(createdDbref).Number;
		await Assert.That(scanPlainText).Contains($"#{dbrefNum}/{attrName}");
	}

	/// <summary>
	/// @scan reports what the dispatcher would actually match, because it runs the same
	/// <c>MatchUserDefinedCommand</c> over the same compiled-pattern cache. That makes it a direct
	/// readout of the <c>\:</c> unescape: before it, a pattern containing an escaped colon reached
	/// .NET with the backslash intact, and for a wildcard that became a character the typed line had
	/// to contain - so @scan reported no match for the one string the command exists to catch, while
	/// <c>IsCommand</c> still counted the attribute as a command.
	/// </summary>
	[Test]
	public async ValueTask ScanCommand_FindsAPatternWithAnEscapedColon()
	{
		var uniqueSuffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var attrName = $"CMD_SCANCOLON_{uniqueSuffix}";
		var commandWord = $"scancolon{uniqueSuffix.ToLowerInvariant()}";

		var createResult = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create ScanColonObj_{uniqueSuffix}"));
		var createdDbref = createResult.Message.ToPlainText();
		await Assert.That(createdDbref).StartsWith("#")
			.Because($"@create should return a dbref; got: '{createdDbref}'");

		// Things are created NO_COMMAND, so nothing on them is scanned until the flag comes off.
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, DBRef.Parse(createdDbref));

		// A client-typed & stores its value as written, so the pattern keeps its \:.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($@"&{attrName} {createdDbref}=${commandWord}\:go *:think scan colon triggered"));

		var scanResult = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@scan {commandWord}:go north"));

		var dbrefNum = DBRef.Parse(createdDbref).Number;
		await Assert.That(scanResult.Message.ToPlainText())
			.Contains($"#{dbrefNum}/{attrName}")
			.Because(@"the stored \: is a literal colon in the pattern, so ""<word>:go north"" matches");

		// The near miss. The colon is the whole point: without it the line is a space away from the
		// pattern and must not match. (There is no companion "typed backslash" case - a command line is
		// evaluated before it is matched, so \\: on the way in collapses to the same : as above.)
		var nearMiss = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@scan {commandWord} go north"));
		await Assert.That(nearMiss.Message.ToPlainText()).DoesNotContain(attrName);
	}

	[Test]
	public async ValueTask DecompileCommand()
	{
		var testPlayer = await CreateWizardAsync("Decompile");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@decompile #1"));

		await Assert.That(Heard(testPlayer.DbRef, message => message.StartsWith("@pcreate God", StringComparison.Ordinal))).Count().IsEqualTo(1);
	}

	/// <summary>
	/// page/noeval with escaped = doesn't crash (PennMUSH testpage.t: page.1 regression).
	/// </summary>
	[Test]
	public async ValueTask PageNoeval_EscapedEquals_DoesNotCrash()
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("page/noeval #1 \\= ="));
		await Assert.That(result).IsNotNull();
	}

	/// <summary>
	/// True when the message went out as markup whose ANSI rendering carries escape codes. A matcher
	/// lambda is an expression tree, which cannot hold the declaration pattern this needs.
	/// </summary>
	private static bool RendersAnsiEscapes(SharpMessage msg) =>
		msg is MString markup && markup.Render(MarkupFormat.Ansi).Contains("\x1b[");
}
