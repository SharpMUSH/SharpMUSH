using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Palette;

/// <summary>
/// The palette against a running engine, each test in a room of its own: what a typed <c>$</c>-command
/// pushes, which entries a player is offered, <c>@context</c> and <c>context()</c>.
/// </summary>
public class PaletteServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IPaletteService Palette => WebAppFactoryArg.Services.GetRequiredService<IPaletteService>();
	private IContextStack Contexts => WebAppFactoryArg.Services.GetRequiredService<IContextStack>();
	private IAttributeService Attributes => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private sealed record Scene(AnySharpObject Player, long Handle, AnySharpObject Thing, AnySharpContainer Room, string Name);

	/// <summary>A room of its own, a connected player in it, and a thing there that God owns.</summary>
	private async Task<Scene> BuildAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var roomRef = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName(prefix + "Room"), god));
		var room = (await Mediator.Send(new GetObjectNodeQuery(roomRef))).Expect<SharpRoom>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, Connections, prefix, roomRef);
		var thingRef = await Mediator.Send(new CreateThingCommand(TestIsolationHelpers.GenerateUniqueName(prefix + "Thing"), room, god, room));
		// Things are made NO_COMMAND by default, and one that answers no $-commands offers no entries.
		using (var budget = new ExecutionBudget(TimeSpan.FromSeconds(30)))
		using (budget.Enter())
		{
			await WebAppFactoryArg.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set {thingRef}=!NO_COMMAND"));
		}

		return new Scene(
			(await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>(),
			player.Handle,
			(await Mediator.Send(new GetObjectNodeQuery(thingRef))).Expect<AnySharpObject>(),
			room,
			player.Name);
	}

	private async Task SetAsync(AnySharpObject target, string attribute, string value)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		(await Attributes.SetAttributeAsync(god, target, attribute, MString.Plain(value))).Expect<Success>();
	}

	private async Task TypeAsync(Scene scene, string line)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var entered = budget.Enter();
		var parser = WebAppFactoryArg.CommandParserFor(scene.Player.Object().DBRef, scene.Handle);
		await parser.CommandParse(scene.Handle, Connections, MarkupText.Plain(line));
	}

	[Test]
	public async ValueTask TypingADollarCommand_PushesItsObject_AndRaisesItsEntries()
	{
		var scene = await BuildAsync("PalTyped");
		var command = "+" + TestIsolationHelpers.GenerateUniqueName("ptest").ToLowerInvariant();
		try
		{
			await SetAsync(scene.Thing, "CMD`PTEST", $"${command}:@pemit %#=ran {command}");
			await SetAsync(scene.Thing, "PALETTE`RUN", $$"""{"label":"Run the test","line":"{{command}}","attr":"CMD`PTEST"}""");

			await TypeAsync(scene, command);

			var system = scene.Thing.Object().DBRef.ToString();
			await Assert.That(Contexts.Read(scene.Player.Object().DBRef).Select(context => context.Name)).Contains(system);
			var suggestion = (await Palette.SuggestAsync(scene.Player)).Single(item => item.Entry.Line == command);
			await Assert.That(suggestion.Entry.Label).IsEqualTo("Run the test");
			await Assert.That(suggestion.Entry.Scope).IsEqualTo(PaletteScope.Room);
			await Assert.That(suggestion.Ready).IsTrue();
		}
		finally
		{
			await Connections.Disconnect(scene.Handle);
		}
	}

	[Test]
	public async ValueTask AnEntry_IsOfferedOnlyWhenItsLockPassesAndItsCommandExists()
	{
		var scene = await BuildAsync("PalLocks");
		try
		{
			await SetAsync(scene.Thing, "CMD`OPEN", "$+palopen:think open");
			await SetAsync(scene.Thing, "PALETTE`OPEN", """{"label":"Open","line":"+palopen","attr":"CMD`OPEN"}""");
			await SetAsync(scene.Thing, "PALETTE`SHUT", """{"label":"Shut","line":"+palopen","attr":"CMD`OPEN","lock":"#FALSE"}""");
			await SetAsync(scene.Thing, "PALETTE`GONE", """{"label":"Gone","line":"+palgone","attr":"CMD`GONE"}""");
			await SetAsync(scene.Thing, "PALETTE`BROKEN", "not json");
			// Entries are one level deep; PALETTE`DEEP`ER is not one, and the empty PALETTE`DEEP it makes is no invalid entry.
			await SetAsync(scene.Thing, "PALETTE`DEEP`ER", """{"label":"Deeper","line":"+palopen","attr":"CMD`OPEN"}""");

			var palette = await Palette.ResolveAsync(scene.Player);
			var offered = palette.Entries.Where(entry => entry.Id.StartsWith(scene.Thing.Object().DBRef + "/")).Select(entry => entry.Label).ToArray();

			await Assert.That(offered).IsEquivalentTo(new[] { "Open" });
			await Assert.That(palette.Invalid).IsEmpty().Because("the player may not examine the thing");

			// Someone who may examine it is told what is wrong.
			using (var budget = new ExecutionBudget(TimeSpan.FromSeconds(30)))
			using (budget.Enter())
			{
				await WebAppFactoryArg.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set {scene.Thing.Object().DBRef}=VISUAL"));
			}

			var examined = await Palette.ResolveAsync(scene.Player);
			await Assert.That(examined.Invalid).Contains($"{scene.Thing.Object().DBRef}/GONE: CMD`GONE is not a $-command");
			await Assert.That(examined.Invalid).Contains($"{scene.Thing.Object().DBRef}/BROKEN: not a JSON object");
			await Assert.That(examined.Invalid.Where(line => line.StartsWith($"{scene.Thing.Object().DBRef}/DEEP"))).IsEmpty();
		}
		finally
		{
			await Connections.Disconnect(scene.Handle);
		}
	}

	[Test]
	public async ValueTask APlayersOwnEntries_AreOfferedToThemAlone()
	{
		var scene = await BuildAsync("PalMine");
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, Connections, "PalOther", scene.Room.Object().DBRef);
		var otherPlayer = (await Mediator.Send(new GetObjectNodeQuery(other.DbRef))).Expect<AnySharpObject>();
		try
		{
			await SetAsync(otherPlayer, "PALETTE`MACRO", """{"label":"My macro","line":"+mymacro"}""");

			var theirs = await Palette.ResolveAsync(otherPlayer);
			await Assert.That(theirs.Entries.Single(entry => entry.Label == "My macro").Scope).IsEqualTo(PaletteScope.Self);

			var mine = await Palette.ResolveAsync(scene.Player);
			await Assert.That(mine.Entries.Any(entry => entry.Label == "My macro")).IsFalse();
		}
		finally
		{
			await Connections.Disconnect(scene.Handle);
			await Connections.Disconnect(other.Handle);
		}
	}

	[Test]
	public async ValueTask APushedContext_FillsSlots_AndContextPassesEachEntryThroughAnAttribute()
	{
		var scene = await BuildAsync("PalPush");
		var marker = TestIsolationHelpers.GenerateUniqueName("ctx");
		try
		{
			await SetAsync(scene.Thing, "PALETTE`DUEL", """{"label":"Duel","line":"+duel <target>","when":["duel"]}""");
			await SetAsync(scene.Thing, "PALETTE`AIM", """{"label":"Aim","line":"+aim <part>","when":["duel"]}""");
			await SetAsync(scene.Player, "FN`ITEM", $"{marker}:%<label>:%<line>:%<ready>:%<contexts>:%<info>");

			await TypeAsync(scene, "@context/push me=duel,[json(object,target,json(string,Bob))],,Your rival waits, sword drawn.");
			await TypeAsync(scene, "think context(me,5,|,me/FN`ITEM)");

			var heard = WebAppFactoryArg.Notifications.For(scene.Player.Object().DBRef).Single(line => line.StartsWith(marker));
			await Assert.That(heard).IsEqualTo(string.Join('|',
				$"{marker}:Aim:+aim <part>:0:duel:Your rival waits, sword drawn.",
				$"{marker}:Duel:+duel Bob:1:duel:Your rival waits, sword drawn."));

			await TypeAsync(scene, $"think {marker}-[context(me)]");
			await Assert.That(WebAppFactoryArg.Notifications.For(scene.Player.Object().DBRef))
				.Contains($"{marker}-Duel").Because("without Send_OOB each ready entry is its label, and Aim is not ready");

			await TypeAsync(scene, "@context/drop me=duel");
			await Assert.That(Contexts.Read(scene.Player.Object().DBRef).Any(context => context.Name == "duel")).IsFalse();
		}
		finally
		{
			await Connections.Disconnect(scene.Handle);
		}
	}

	[Test]
	public async ValueTask APlayer_MayNotPushOntoAnotherPlayersStack()
	{
		var scene = await BuildAsync("PalDeny");
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, Connections, "PalDenied", scene.Room.Object().DBRef);
		try
		{
			await TypeAsync(scene, $"@context/push {other.Name}=pranked");

			await Assert.That(WebAppFactoryArg.Notifications.For(scene.Player.Object().DBRef)).Contains(ErrorMessages.Returns.PermissionDenied);
			var otherPlayer = (await Mediator.Send(new GetObjectNodeQuery(other.DbRef))).Expect<AnySharpObject>();
			await Assert.That(Contexts.Read(otherPlayer.Object().DBRef)).IsEmpty();
		}
		finally
		{
			await Connections.Disconnect(scene.Handle);
			await Connections.Disconnect(other.Handle);
		}
	}
}
