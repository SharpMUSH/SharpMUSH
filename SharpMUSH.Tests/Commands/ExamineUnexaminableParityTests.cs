using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>do_examine</c> for a viewer who may not examine what they are looking at
/// (<c>src/look.c:802-823</c> and <c>:896-909</c>): standing next to it and with
/// <c>ex_public_attribs</c> on they get the <c>DESCRIBE</c> value and whatever attributes they may
/// read, then <c>&lt;object_header&gt; is owned by &lt;object_header&gt;</c> — with no full stop,
/// because the line is assembled from two <c>safe_str</c> calls and never punctuated. Off, or from a
/// distance, that owner line is the whole answer. An attribute pattern is answered before either test
/// (<c>:796-801</c>) and prints nothing else.
/// </summary>
public class ExamineUnexaminableParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private TestIsolationHelpers.TestPlayer _mortal = null!;
	private SharpPlayer _god = null!;
	private DBRef _room;
	private DBRef _elsewhere;

	/// <summary>
	/// The mortal stands in a room of its own, so no other test's connect announcements land in the
	/// notifications these assertions read.
	/// </summary>
	[Before(Test)]
	public async Task CreateFixtures()
	{
		_god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		_room = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("ExamMortalRoom"), _god));
		_elsewhere = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("ExamFarRoom"), _god));
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamineMortal", _room);
	}

	[After(Test)]
	public async Task DisconnectPlayer()
	{
		if (_mortal is not null)
			await ConnectionService.Disconnect(_mortal.Handle);
	}

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_mortal.DbRef, _mortal.Handle);

	private int _notificationOffset;

	// The plain-text recorder, not RawFor: a localized notification is formatted before it reaches the
	// substitute, so "No matching attributes." is only ever recorded here.
	private string Output => string.Join("\n", WebAppFactoryArg.Notifications.For(_mortal.DbRef)
		.Skip(_notificationOffset));

	private async Task<string> RunCaptured(string command)
	{
		_notificationOffset = WebAppFactoryArg.Notifications.CountFor(_mortal.DbRef);
		await Parser.CommandParse(_mortal.Handle, ConnectionService, MarkupText.Plain(command));
		return Output;
	}

	/// <summary>
	/// A thing God owns, described, carrying one attribute the mortal may not read — the parity
	/// harness's Widget, which is what <c>10-player-commands/obj.inventory#2</c> examines.
	/// </summary>
	private async Task<(DBRef DbRef, string Name)> CreateGodOwnedThingAsync(DBRef where)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("ExamWidget");
		var container = (await Mediator.Send(new GetObjectNodeQuery(where))).Expect<AnySharpObject>().AsOptionalContainer.Expect<AnySharpContainer>();
		var thing = await Mediator.Send(new CreateThingCommand(name, container, _god, container));

		var god = WebAppFactoryArg.CommandParser;
		await god.CommandParse(1, ConnectionService, MarkupText.Plain($"@desc #{thing.Number}=A small widget."));
		await god.CommandParse(1, ConnectionService, MarkupText.Plain($"&COLOR #{thing.Number}=blue"));

		return (thing, name);
	}

	[Test]
	public async Task Examine_OfACarriedObjectSomeoneElseOwns_IsTheDescriptionAndAnUnpunctuatedOwnerLine()
	{
		var (thing, name) = await CreateGodOwnedThingAsync(_room);

		var output = await RunCaptured($"examine #{thing.Number}");

		await Assert.That(output).IsEqualTo($"A small widget.\n{name} is owned by {_god.Object.Name}")
			.Because("look.c:832-839 prints DESCRIBE, then :900-905 appends the owner line with no full stop");
	}

	[Test]
	public async Task Examine_OfAnObjectSomeoneElseOwnsElsewhere_IsTheOwnerLineAlone()
	{
		var (thing, name) = await CreateGodOwnedThingAsync(_elsewhere);

		var output = await RunCaptured($"examine #{thing.Number}");

		await Assert.That(output).IsEqualTo($"{name} is owned by {_god.Object.Name}")
			.Because("nearby() fails, so look.c:809-823 returns before the description");
	}

	[Test]
	public async Task Examine_WithExPublicAttribsOff_IsTheOwnerLineAlone()
	{
		var (thing, name) = await CreateGodOwnedThingAsync(_room);

		string output;
		using (TestOptionsOverride.Scope(options => options with
		{
			Cosmetic = options.Cosmetic with { ExaminePublicAttributes = false }
		}))
		{
			output = await RunCaptured($"examine #{thing.Number}");
		}

		await Assert.That(output).IsEqualTo($"{name} is owned by {_god.Object.Name}")
			.Because("EX_PUBLIC_ATTRIBS off takes the same early return as being far away (look.c:809-823)");
	}

	/// <summary>
	/// Contents walk <c>DOLIST_VISIBLE</c> (<c>look.c:885</c>), so <c>first_visible</c>'s DARK rules
	/// apply (<c>src/predicat.c:1130-1160</c>), and each line is <c>object_header</c> of the *content*
	/// (<c>:893</c>) — which hides a dbref the viewer is not allowed to see (<c>src/unparse.c:118-119</c>).
	/// </summary>
	[Test]
	public async Task Examine_OfANearbyContainerSomeoneElseOwns_HidesDarkContentsAndDbrefs()
	{
		var room = (await Mediator.Send(new GetObjectNodeQuery(_room))).Expect<AnySharpObject>().AsOptionalContainer.Expect<AnySharpContainer>();
		var boxName = TestIsolationHelpers.GenerateUniqueName("ExamBox");
		var box = await Mediator.Send(new CreateThingCommand(boxName, room, _god, room));
		var inside = (await Mediator.Send(new GetObjectNodeQuery(box))).Expect<AnySharpObject>().AsOptionalContainer.Expect<AnySharpContainer>();

		var plainName = TestIsolationHelpers.GenerateUniqueName("ExamPlain");
		await Mediator.Send(new CreateThingCommand(plainName, inside, _god, inside));
		var darkName = TestIsolationHelpers.GenerateUniqueName("ExamDark");
		var dark = await Mediator.Send(new CreateThingCommand(darkName, inside, _god, inside));
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set #{dark.Number}=DARK"));

		var output = await RunCaptured($"examine #{box.Number}");

		await Assert.That(output).IsEqualTo($"Contents:\n{plainName}\n{boxName} is owned by {_god.Object.Name}")
			.Because("the DARK content is not listed at all, and the other is listed by bare name");
	}

	[Test]
	public async Task ExamineOfAnAttributeTheViewerCannotRead_SaysNoMatchingAttributes()
	{
		var (thing, _) = await CreateGodOwnedThingAsync(_room);

		var output = await RunCaptured($"examine #{thing.Number}/COLOR");

		await Assert.That(output).IsEqualTo("No matching attributes.")
			.Because("an attribute pattern returns at look.c:796-801, and examine_atrs answers a named "
				+ "pattern that matched nothing with this line (look.c:384) — no header, no owner line");
	}

	[Test]
	public async Task ExamineOfAnAttributeTheOwnerCanRead_IsTheAttributeAlone()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("ExamOwn");
		var container = (await Mediator.Send(new GetObjectNodeQuery(_room))).Expect<AnySharpObject>().AsOptionalContainer.Expect<AnySharpContainer>();
		var owned = (await Mediator.Send(new GetObjectNodeQuery(_mortal.DbRef))).Expect<SharpPlayer>();
		var thing = await Mediator.Send(new CreateThingCommand(name, container, owned, container));
		await Parser.CommandParse(_mortal.Handle, ConnectionService,
			MarkupText.Plain($"@desc #{thing.Number}=A thing of mine."));
		await Parser.CommandParse(_mortal.Handle, ConnectionService,
			MarkupText.Plain($"&COLOR #{thing.Number}=green"));

		var output = await RunCaptured($"examine #{thing.Number}/COLOR");

		await Assert.That(output).IsEqualTo($"COLOR [#{_mortal.DbRef.Number}]: green")
			.Because("examine_atrs returns on its own, so the pattern form prints neither the header "
				+ "block nor the description (look.c:796-801)");
	}
}
