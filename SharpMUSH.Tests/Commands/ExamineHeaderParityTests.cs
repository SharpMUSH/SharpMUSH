using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>brief</c> is <c>do_examine(..., EXAM_BRIEF, ...)</c> (<c>src/cmds.c:1654-1657</c>), and every
/// object line examine prints is <c>object_header</c>, i.e. <c>Name(#N&lt;flags&gt;)</c>
/// (<c>src/look.c:780-990</c>, <c>src/unparse.c:91-140</c>). <c>FLAGS_ON_EXAMINE</c> only gates the
/// <c>Type: ... Flags: ...</c> line; <c>EXAM_BRIEF</c> only skips the attribute list.
/// </summary>
public class ExamineHeaderParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private TestIsolationHelpers.TestPlayer _player = null!;

	[Before(Test)]
	public async Task CreatePlayer()
	{
		// Assertions read this player's notifications, so it stands in a room of its own where no
		// other test's connect announcements land.
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var room = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("ExamHeaderRoom"), god));
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamineHeaderParity", room);
		// A wizard, so it may @open in the god-owned room it stands in.
		var player = (await Mediator.Send(new GetObjectNodeQuery(_player.DbRef))).Expect<SharpPlayer>();
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(player, wizard!))).IsTrue();
	}

	[After(Test)]
	public async Task DisconnectPlayer()
	{
		if (_player is not null)
			await ConnectionService.Disconnect(_player.Handle);
	}

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_player.DbRef, _player.Handle);

	private int _notificationOffset;

	private string Output => string.Join("\n", WebAppFactoryArg.Notifications.RawFor(_player.DbRef)
		.Skip(_notificationOffset)
		.Select(m => m switch { MString ms => ms.ToPlainText(), string s => s }));

	private async ValueTask Run(string command) =>
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain(command));

	private async ValueTask<string> RunCaptured(string command)
	{
		_notificationOffset = WebAppFactoryArg.Notifications.RawCountFor(_player.DbRef);
		await Run(command);
		return Output;
	}

	private async Task<SharpObject> CreateThingAsync(string prefix)
	{
		var result = await TestIsolationHelpers.CreateObjectCommandAsync(Parser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName(prefix), _player.Handle);
		var dbref = DBRef.Parse(result.Message.ToPlainText());
		return (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>().Object();
	}

	/// <summary><c>object_header</c> as the test player sees it: CONNECTED shows on itself.</summary>
	private async Task<string> Header(DBRef dbref) =>
		await MessageFormatting.FormatObjectWithDbref(
			(await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>().Object(),
			await FlagView.ForAsync((await Mediator.Send(new GetObjectNodeQuery(_player.DbRef))).Expect<AnySharpObject>(),
				ConnectionService));

	[Test]
	public async Task Brief_NameAndOwnerLinesAreObjectHeaders()
	{
		var thing = await CreateThingAsync("BriefHdr");
		await Run($"@set {thing.DBRef}=VISUAL");
		var thingHeader = await Header(thing.DBRef);
		var ownerHeader = await Header(_player.DbRef);

		var output = await RunCaptured($"brief {thing.DBRef}");

		await Assert.That(output.Split('\n')).Contains(thingHeader)
			.Because($"brief's first line is object_header(thing); actual output: {output}");
		await Assert.That(output).Contains($"Owner: {ownerHeader}")
			.Because($"the owner line is object_header(owner), plain text; actual output: {output}");
		await Assert.That(output).Contains("Warnings checked:")
			.Because("brief is examine without attributes, so it prints examine's header block");
	}

	[Test]
	public async Task Brief_OmitsAttributesButListsCarriedObjects()
	{
		var carried = await CreateThingAsync("BriefCarried");
		await Run("&BRIEFATTR me=briefvalue");

		var output = await RunCaptured("brief me");

		await Assert.That(output).DoesNotContain("BRIEFATTR")
			.Because("EXAM_BRIEF skips the attribute list");
		await Assert.That(output).Contains($"Carrying:\n{await Header(carried.DBRef)}")
			.Because($"contents are listed with object_header; actual output: {output}");
	}

	[Test]
	public async Task ExamineBrief_StillListsContentsExitsAndHome()
	{
		var carried = await CreateThingAsync("ExamBriefCarried");

		var self = await RunCaptured("examine/brief me");
		await Assert.That(self).Contains($"Carrying:\n{await Header(carried.DBRef)}")
			.Because($"EXAM_BRIEF still lists contents (look.c:882-895); actual output: {self}");
		await Assert.That(self).Contains("Home: ")
			.Because("EXAM_BRIEF still prints home and location (look.c:922-930)");

		var exitName = TestIsolationHelpers.GenerateUniqueName("ExamBriefExit");
		await Run($"@open {exitName}");
		var room = await RunCaptured("examine/brief here");
		await Assert.That(room).Contains("Exits:\n")
			.Because($"EXAM_BRIEF still lists a room's exits (look.c:909-916); actual output: {room}");
		await Assert.That(room.Split('\n').Count(line => line.Contains(exitName))).IsEqualTo(1)
			.Because($"Contents(thing) never holds exits, so an exit is listed only under Exits:; actual output: {room}");
	}

	[Test]
	public async Task Examine_ThingContainingObjects_LabelsThemContents()
	{
		var box = await CreateThingAsync("ExamBox");
		var inner = await CreateThingAsync("ExamInner");
		await Run($"@tel {inner.DBRef}={box.DBRef}");

		var output = await RunCaptured($"examine {box.DBRef}");

		await Assert.That(output).Contains($"Contents:\n{await Header(inner.DBRef)}")
			.Because($"only a player's contents are labelled Carrying: (look.c:887-890); actual output: {output}");
		await Assert.That(output).DoesNotContain("Carrying:");
	}

	[Test]
	public async Task FlagsOnExamineOff_KeepsHeaderFlagsAndDropsTypeLine()
	{
		var thing = await CreateThingAsync("ExamNoFlags");
		await Run($"@set {thing.DBRef}=VISUAL");
		var thingHeader = await Header(thing.DBRef);
		await Assert.That(thingHeader).Contains("V");

		string output;
		using (TestOptionsOverride.Scope(options => options with
		{
			Cosmetic = options.Cosmetic with { FlagsOnExamine = false }
		}))
		{
			output = await RunCaptured($"examine {thing.DBRef}");
		}

		await Assert.That(output).Contains(thingHeader)
			.Because($"object_header always carries the flag symbols; actual output: {output}");
		await Assert.That(output).DoesNotContain("Type: ")
			.Because("FLAGS_ON_EXAMINE gates the whole flag_description line, Type included (look.c:829-830)");
	}
}
