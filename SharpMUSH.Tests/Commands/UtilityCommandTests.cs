using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
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

	[Test]
	public async ValueTask ThinkBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("think ThinkBasic Test output"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString, string>>(x
				=> x.Value.ToString()!.Contains("ThinkBasic Test output")));
	}

	[Test]
	public async ValueTask ThinkWithFunction()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("think ThinkWithFunction [add(2,3)]"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf.OneOf<MString, string>>(x
					=> x.Value.ToString()!.Contains("ThinkWithFunction 5")));
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask CommentCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@@ This is a comment"));

		// Comment should not produce any output
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf.OneOf<MString, string>>(x
				=> x.Value.ToString()!.Contains("This is a comment")));
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask LookBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("look"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask LookAtObject()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("look #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsNameAndDbref()
	{
		// Verify the name row has "Name(#dbref)" format (no space before '(') in plain text.
		// We use plain-text check because name.Hilight() inserts ANSI codes around the name.
		// Player #1 is named "God" in the test database.
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "God(#1")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsOwnerRow()
	{
		// Owner row uses proper MModule composition; plain-text must contain "Owner: "
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Owner: ")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsZoneAndPowers()
	{
		// Zone and Powers are always shown (even when empty/nothing)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Zone: *NOTHING*")));
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Powers: ")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsWarningsChecked()
	{
		// "Warnings checked:" is always shown (even when empty)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Warnings checked:")));
	}

	[Test]
	public async ValueTask ExamineObject_HeaderContainsLastModified()
	{
		// "Last modified:" is always shown in both examine and brief
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Last modified:")));
	}

	[Test]
	public async ValueTask ExaminePlayer_HeaderContainsQuota()
	{
		// "Quota:" is shown for player objects (God is player #1)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Quota:")));
	}

	[Test]
	public async ValueTask ExamineRoom_ShowsExits()
	{
		// Dig a room with exits; the new room gets the return exit → examine should show Exits:
		var digResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@dig ExitTestSource=North;N,South;S"));
		var digMessage = digResult?.Message?.ToPlainText();
		await Assert.That(digMessage).IsNotNull();
		var roomDbRef = DBRef.Parse(digMessage!);

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"examine {roomDbRef}"));

		// The Exits: section should appear because the new room has the return exit (South;S)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Exits:")));
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_AlsoShowsLastModified()
	{
		// Brief mode should also show Last modified: (it's a header field)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine/brief #1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Last modified:")));
	}

	[Test]
	public async ValueTask ExamineObject_AttributeWithAnsi_PreservesMarkup()
	{
		// Create an object, set a DESCRIBE with ANSI color, then examine it.
		// The attribute value (an MString with markup) must survive through examine output.
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@create AnsiExamineTestObj"));
		var objDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// [ansi(rh,...)] evaluates to an MString with red+bold markup
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@desc {objDbRef}=[ansi(rh,AnsiColorText)]"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"examine {objDbRef}"));

		// Plain-text content of the attribute value must appear in examine output
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "AnsiColorText")));

		// The ANSI-rendered output must contain actual ANSI escape codes
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "AnsiColorText") &&
				msg.IsT0 && msg.AsT0.ToString().Contains("\x1b[")));
	}

	[Test]
	public async ValueTask ExamineObject_BriefSwitch_ShowsHeaderNotDescription()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@create BriefExamineTestObj"));
		var objDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@desc {objDbRef}=BriefShouldNotSeeThis"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"examine/brief {objDbRef}"));

		// Brief MUST show owner header (in plain text because owner name is hilighted)
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Owner: ")));

		// Brief must NOT show description text
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "BriefShouldNotSeeThis")));
	}

	[Test]
	public async ValueTask ExamineObjectOpaqueSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine/opaque #1"));

		// /opaque should still show header
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask ExamineWithAttributePattern()
	{
		// Test examining with attribute pattern (e.g., examine #1/DESC*)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine #1/DESC*"));

		// Should display matching attributes
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask ExamineCurrentLocation()
	{
		// Test examining with no argument (examines current location)
		await Parser.CommandParse(1, ConnectionService, MModule.single("examine"));

		// Should display current location
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask FindCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@find #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SearchCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@search"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EntrancesCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@entrances #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask StatsCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@stats"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask VersionCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@version"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Explicit("Command is implemented but test is failing")]
	public async ValueTask ScanCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@scan"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask DecompileCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@decompile #1"));

		// Should receive notifications for decompiled output
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(),
				Arg.Any<OneOf.OneOf<MString, string>>(),
				Arg.Any<AnySharpObject?>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhereisCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@whereis #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
