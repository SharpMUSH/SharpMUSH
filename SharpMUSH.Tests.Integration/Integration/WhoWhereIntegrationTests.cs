using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled who-where package's +who and +where, installed from the catalogue and driven with typed commands:
/// +who by name, by idle or matching a name; +where grouped under a line per location, one player per line by
/// location or by name, and a player in an unfindable room. The shared
/// world has other tests' players connected, so the tests look at their own players' rows and the order between
/// them, never at the whole list. Not in parallel: +where is global, and the status and watch tests install who-where too.
/// </summary>
[NotInParallel]
public class WhoWhereIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private string Functions { get; set; } = string.Empty;

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>A new room, as its dbref.</summary>
	private async Task<(string DbRef, string Name)> Room(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var room = await Mediator.Send(new CreateRoomCommand(name, god));
		return ($"#{room.Number}", name);
	}

	/// <summary>A connected player standing in <paramref name="room"/>.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix, string room)
	{
		var home = new DBRef(int.Parse(room.TrimStart('#')));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	/// <summary>The line of <paramref name="screen"/> that holds <paramref name="text"/>.</summary>
	private static string LineWith(string screen, string text)
		=> screen.Split('\n').Single(line => line.Contains(text, StringComparison.Ordinal));

	/// <summary>Whether each of <paramref name="texts"/> first appears in <paramref name="screen"/> after the one before.</summary>
	private static bool Order(string screen, params string[] texts)
	{
		var at = texts.Select(text => screen.IndexOf(text, StringComparison.Ordinal)).ToList();
		return at.All(index => index >= 0) && at.Zip(at.Skip(1)).All(pair => pair.First < pair.Second);
	}

	/// <summary>
	/// A table's text with its spaces, borders and line breaks taken out, so a cell the column wrapped reads
	/// whole. How wide the Who column is depends on the other rooms in the shared world.
	/// </summary>
	private static string Unwrapped(string table)
		=> new(table.Where(c => !char.IsWhiteSpace(c) && c != '|').ToArray());

	private async Task InstallAsync()
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "who-where", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("who-where must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
		Functions = (await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("who-where"))
			.First(o => o.Ref == "ww_functions").Objid;
	}

	private Task UninstallAsync() => Installer.UninstallAsync("who-where", force: true, CancellationToken.None);

	[Test]
	public async Task WhereGroupsByLocationAndSortsByLocationOrName()
	{
		try
		{
			await InstallAsync();
			var (parlour, parlourName) = await Room("WgParlour");
			var (attic, atticName) = await Room("WgAttic");
			// Created out of name order, so a list in creation order would fail the sorted checks.
			var cy = await Player("WgCy", parlour);
			var ann = await Player("WgAnn", parlour);
			var bo = await Player("WgBo", attic);
			var players = $"#{cy.DbRef.Number} #{ann.DbRef.Number} #{bo.DbRef.Number}";

			// The grouping: each room once, its players by name; then the rooms by name.
			var groups = $"u({Functions}/WW_FN_GROUPS,{players})";
			await Assert.That(await God($"think {groups}"))
				.IsEqualTo($"{{\"{parlour}\":[\"#{ann.DbRef.Number}\",\"#{cy.DbRef.Number}\"],\"{attic}\":[\"#{bo.DbRef.Number}\"]}}");
			await Assert.That(await God($"think u({Functions}/WW_FN_ROOMS,{groups})")).IsEqualTo($"{attic} {parlour}");
			await Assert.That(await God($"think u({Functions}/WW_FN_IN,{parlour},{groups})"))
				.IsEqualTo($"#{ann.DbRef.Number} #{cy.DbRef.Number}");

			// +where: a line per location with its count, rooms by name, and the players under it by name.
			var grouped = await As(ann, "+where");
			await Assert.That(grouped).Contains("Where").And.Contains("connected in");
			await Assert.That(LineWith(grouped, parlourName)).Contains("2 players");
			await Assert.That(LineWith(grouped, atticName)).Contains("1 player");
			await Assert.That(Order(grouped, atticName, bo.Name, parlourName, ann.Name, cy.Name)).IsTrue();

			// +where/room: one player per line, the attic's before the parlour's, and Ann before Cy.
			var byRoom = await As(ann, "+where/room");
			await Assert.That(byRoom).Contains("Where by location");
			await Assert.That(LineWith(byRoom, ann.Name)).Contains(parlourName);
			await Assert.That(Order(byRoom, bo.Name, ann.Name, cy.Name)).IsTrue();

			// +where/name: one player per line, by name.
			var byName = await As(ann, "+where/name");
			await Assert.That(byName).Contains("Where by name");
			await Assert.That(LineWith(byName, bo.Name)).Contains(atticName);
			await Assert.That(Order(byName, ann.Name, bo.Name, cy.Name)).IsTrue();

			await Assert.That(await As(ann, "+where/bogus")).Contains("+where has no /bogus switch.");
			await Assert.That(await As(ann, "+help where")).Contains("+where/room").And.Contains("+where/name");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task WhoListsByNameOrIdleAndMatchesANameOrAlias()
	{
		try
		{
			await InstallAsync();
			var (den, denName) = await Room("WhDen");
			var cy = await Player("WhCy", den);
			var ann = await Player("WhAnn", den);
			// An alias is checked as a player name, at most player_name_len (15) characters; a generated name is longer.
			var alias = $"Wz{Guid.NewGuid():N}"[..12];
			await God($"&ALIAS #{cy.DbRef.Number}={alias}");
			await Assert.That(await God($"think get(#{cy.DbRef.Number}/ALIAS)")).IsEqualTo(alias);

			var who = await As(ann, "+who");
			await Assert.That(who).Contains("Who's Online").And.Contains("Location").And.Contains("connected");
			await Assert.That(Order(who, ann.Name, cy.Name)).IsTrue();

			// +who <name> matches the start of a name or of an alias, and counts what it left out. One player
			// leaves room for every column; the whole list may not, with the shared world's long names.
			var byAlias = await As(ann, $"+who {alias}");
			await Assert.That(LineWith(byAlias, cy.Name)).Contains(alias).And.Contains(denName);
			await Assert.That(byAlias).DoesNotContain(ann.Name).And.Contains("1 of ");
			await Assert.That(await As(ann, $"+who {ann.Name}")).Contains(ann.Name).And.DoesNotContain(cy.Name);
			await Assert.That(await As(ann, "+who WzNobodyHasThis")).Contains("Nobody connected has a name starting with WzNobodyHasThis.");
			// A * is a character to match, not a wildcard.
			await Assert.That(await As(ann, "+who Wz*")).Contains("Nobody connected has a name starting with Wz*.");

			await Assert.That(await As(ann, "+who/idle")).Contains(ann.Name).And.Contains(cy.Name);
			await Assert.That(await As(ann, "+who/bogus")).Contains("+who has no /bogus switch.");
			await Assert.That(await As(ann, "+help who")).Contains("+who/idle");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task APlayerInAnUnfindableRoomIsListedAtADash()
	{
		try
		{
			await InstallAsync();
			var (hideout, hideoutName) = await Room("WuHideout");
			var (hall, _) = await Room("WuHall");
			var hidden = await Player("WuHidden", hideout);
			var reader = await Player("WuReader", hall);
			await God($"@set {hideout}=UNFINDABLE");

			var byName = await As(reader, "+where/name");
			await Assert.That(LineWith(byName, hidden.Name)).DoesNotContain(hideoutName).And.Contains(" - ");
			await Assert.That(byName).DoesNotContain(hideoutName);
			var grouped = await As(reader, "+where");
			await Assert.That(grouped).DoesNotContain(hideoutName);
			await Assert.That(Order(grouped, "Unfindable", hidden.Name)).IsTrue();

			// +where locates players as the function object does, so the room stays hidden from God too.
			await Assert.That(await God($"think u({Functions}/WW_FN_GROUPS,#{hidden.DbRef.Number})"))
				.IsEqualTo($"{{\"#-1\":[\"#{hidden.DbRef.Number}\"]}}");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>Large data: two hundred players spread over twenty rooms.</summary>
	[Test]
	public async Task ManyPlayersGroupIntoTheirRooms()
	{
		try
		{
			await InstallAsync();
			var rooms = new List<string>();
			for (var count = 0; count < 20; count++)
			{
				rooms.Add((await Room("WsRoom")).DbRef);
			}

			var players = new List<TestIsolationHelpers.TestPlayer>();
			for (var count = 0; count < 200; count++)
			{
				players.Add(await Player("WsP", rooms[count % rooms.Count]));
			}

			var list = string.Join(' ', players.Select(p => $"#{p.DbRef.Number}"));
			var groups = $"u({Functions}/WW_FN_GROUPS,{list})";
			await Assert.That(await God($"think words(u({Functions}/WW_FN_ROOMS,{groups}))")).IsEqualTo("20");
			foreach (var room in rooms)
			{
				await Assert.That(await God($"think words(u({Functions}/WW_FN_IN,{room},{groups}))")).IsEqualTo("10");
			}

			var grouped = await As(players[0], "+where");
			await Assert.That(grouped).Contains("connected");
			await Assert.That(await As(players[0], "+where/room")).Contains(players[199].Name);
			await Assert.That(await As(players[0], "+where/name")).Contains(players[199].Name);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>
	/// Large data, the costly shape: every player in a room of their own. Each screen is sent in parts through
	/// @dolist, so the command's own evaluation costs a few function calls (<c>%?</c>) a player and each part's are
	/// its own. At 33 a player in one evaluation (#1865), +where passed function_invocation_limit (25000) with
	/// about 760 players connected.
	/// </summary>
	[Test]
	public async Task EveryScreenCostsItsCommandAFewCallsPerPlayer()
	{
		try
		{
			await InstallAsync();
			var players = new List<TestIsolationHelpers.TestPlayer>();
			for (var count = 0; count < 150; count++)
			{
				players.Add(await Player("WcP", (await Room("WcRoom")).DbRef));
			}

			var list = string.Join(' ', players.Select(p => $"#{p.DbRef.Number}"));
			foreach (var screen in new[] { "WW_FN_WHERE", "WW_FN_WHERE_ROOM", "WW_FN_WHERE_NAME", "WW_FN_WHO" })
			{
				// %? is the calls made so far, then the depth; the few around the screen count too.
				var calls = await God($"think [setq(0,u({Functions}/{screen},{list}))][first(%?)]");
				await Assert.That(int.Parse(calls)).IsLessThan(players.Count * 5)
					.Because($"{screen} must cost its command fewer than 5 calls a player");
			}

			// Small parts, so every screen is built across several runs and the rows meet at part boundaries.
			await God($"&WW_PART_PLAYERS {Functions}=40");
			await God($"&WW_PART_ROOMS {Functions}=40");
			foreach (var command in new[] { "+where", "+where/room", "+where/name", "+who", "+who/idle", "+who WcP" })
			{
				var screen = await As(players[0], command);
				await Assert.That(screen).Contains(players[149].Name).And.Contains(players[0].Name)
					.And.DoesNotContain("LIMIT EXCEEDED").And.DoesNotContain("#-1").Because(command);
			}
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
