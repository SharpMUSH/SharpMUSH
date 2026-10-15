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
/// The bundled who-where package's +where, installed from the catalogue and driven with typed commands: grouped
/// by location, one player per line by location or by name, and a player in an unfindable room. The shared
/// world has other tests' players connected, so the tests look at their own players' rows and the order between
/// them, never at the whole list. Not in parallel: +where is global, and rp-finder's tests install who-where too.
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
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>());
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

			// The grouping, as God reads it: rooms by name, and each room's players by name.
			var pairs = await God($"think u({Functions}/WW_FN_PAIRS,{players})");
			await Assert.That(await God($"think u({Functions}/WW_FN_ROOMS,{pairs})")).IsEqualTo($"{attic} {parlour}");
			await Assert.That(await God($"think u({Functions}/WW_FN_IN,{parlour},{pairs})"))
				.IsEqualTo($"#{ann.DbRef.Number} #{cy.DbRef.Number}");

			// +where: a row per location, with the count and the names.
			var grouped = await As(ann, "+where");
			await Assert.That(grouped).Contains("Where").And.Contains("Location").And.Contains("Players").And.Contains("connected");
			await Assert.That(LineWith(grouped, ann.Name)).Contains(" 2 ");
			await Assert.That(Unwrapped(grouped)).Contains($"{ann.Name},{cy.Name}");
			await Assert.That(LineWith(grouped, bo.Name)).Contains(" 1 ");

			// +where/room: one player per line, the attic's before the parlour's, and Ann before Cy.
			var byRoom = await As(ann, "+where/room");
			await Assert.That(byRoom).Contains("Where by location");
			await Assert.That(LineWith(byRoom, ann.Name)).Contains(parlourName);
			await Assert.That(byRoom.IndexOf(bo.Name, StringComparison.Ordinal))
				.IsLessThan(byRoom.IndexOf(ann.Name, StringComparison.Ordinal));
			await Assert.That(byRoom.IndexOf(ann.Name, StringComparison.Ordinal))
				.IsLessThan(byRoom.IndexOf(cy.Name, StringComparison.Ordinal));

			// +where/name: one player per line, by name.
			var byName = await As(ann, "+where/name");
			await Assert.That(byName).Contains("Where by name");
			await Assert.That(LineWith(byName, bo.Name)).Contains(atticName);
			await Assert.That(byName.IndexOf(ann.Name, StringComparison.Ordinal))
				.IsLessThan(byName.IndexOf(bo.Name, StringComparison.Ordinal));
			await Assert.That(byName.IndexOf(bo.Name, StringComparison.Ordinal))
				.IsLessThan(byName.IndexOf(cy.Name, StringComparison.Ordinal));

			await Assert.That(await As(ann, "+where/bogus")).Contains("+where has no /bogus switch.");
			await Assert.That(await As(ann, "+help where")).Contains("+where/room").And.Contains("+where/name");
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
			await Assert.That(LineWith(grouped, hidden.Name)).Contains(" - ");

			// +where locates players as the function object does, so the room stays hidden from God too.
			var pairs = await God($"think u({Functions}/WW_FN_PAIRS,#{hidden.DbRef.Number})");
			await Assert.That(pairs).IsEqualTo($"#-1:#{hidden.DbRef.Number}");
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
			var pairs = await God($"think u({Functions}/WW_FN_PAIRS,{list})");
			await Assert.That(await God($"think words(u({Functions}/WW_FN_ROOMS,{pairs}))")).IsEqualTo("20");
			foreach (var room in rooms)
			{
				await Assert.That(await God($"think words(u({Functions}/WW_FN_IN,{room},{pairs}))")).IsEqualTo("10");
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
}
