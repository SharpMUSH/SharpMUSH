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
/// The bundled watch package, installed from the catalogue with who-where and driven with typed commands: the
/// watch list fed by the master room's ACONNECT and ADISCONNECT. Each test installs watch and removes it again.
/// Nothing waits on the clock: a connect outside the quiet window is written onto the store. Not in parallel:
/// +watch and +who are global.
/// </summary>
[NotInParallel]
public class WatchIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private string Store { get; set; } = string.Empty;

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

	/// <summary>The lines <paramref name="player"/> heard that contain <paramref name="text"/>.</summary>
	private List<string> Heard(TestIsolationHelpers.TestPlayer player, string text)
		=> Notifications.For(player.DbRef).Where(line => line.Contains(text, StringComparison.Ordinal)).ToList();

	/// <summary>A connected player in a room of its own, holding <paramref name="role"/> when one is named.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix, string? role = null)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		if (role is not null) await God($"@role/assign #{player.DbRef.Number}={role}");
		return player;
	}

	/// <summary>Logs <paramref name="player"/> in on a new connection, as their first one, and runs the connect hooks.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Connect(TestIsolationHelpers.TestPlayer player)
	{
		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player.DbRef);
		var playerObject = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
		await WebAppFactoryArg.Services.GetRequiredService<IConnectionAnnounceService>()
			.AnnounceConnectAsync(playerObject, connectionCount: 1, isHiddenConnection: false);
		await WebAppFactoryArg.QueueBarrierAsync();
		return player with { Handle = handle };
	}

	/// <summary>Closes <paramref name="player"/>'s connection, which runs the disconnect hooks.</summary>
	private async Task Disconnect(TestIsolationHelpers.TestPlayer player)
	{
		await ConnectionService.Disconnect(player.Handle);
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	/// <summary>
	/// A table's text with its spaces, borders and line breaks taken out, so a cell the column wrapped reads
	/// whole. The shared world's long player names leave the Status column narrow.
	/// </summary>
	private static string Unwrapped(string table)
		=> new(table.Where(c => !char.IsWhiteSpace(c) && c != '|').ToArray());

	/// <summary>The key a player's records are filed under on the store.</summary>
	private async Task<string> Key(TestIsolationHelpers.TestPlayer player)
		=> await God($"think edit(objid(#{player.DbRef.Number}),#,,:,_)");

	/// <summary>Installs who-where and watch; each test removes both.</summary>
	private async Task InstallAsync()
	{
		await ApplyAsync("who-where");
		await ApplyAsync("watch");
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("watch");
		Store = objects.First(o => o.Ref == "store").Objid;
	}

	private async Task ApplyAsync(string package)
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync()
	{
		await WebAppFactoryArg.Services.UninstallWithBridgesAsync("watch");
		await WebAppFactoryArg.Services.UninstallWithBridgesAsync("who-where");
	}

	[Test]
	public async Task SwitchesPagesAndHelpAreChecked()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("SwAnn");

			await Assert.That(await As(ann, "+watch/bogus")).Contains("+help watch lists them.");
			await Assert.That(await As(ann, "+watch/add/del x")).Contains("Use one switch at a time.");
			await Assert.That(await As(ann, "+watch/add/2 x")).Contains("+watch/add takes no page number.");
			await Assert.That(await As(ann, "+watch/list/0")).Contains("Pages start at 1");
			await Assert.That(await As(ann, "+watch something")).Contains("+watch/add <name> watches someone.");

			await Assert.That(await As(ann, "+help watch")).Contains("+watch/watchers");
			// The staff topic is locked to config.admin, which the settings it documents need.
			await Assert.That(await As(ann, "+help watch staff")).DoesNotContain("+config watch");
			await God($"@permission/allow #{ann.DbRef.Number}=config.admin");
			await Assert.That(await As(ann, "+help watch staff")).Contains("+config watch");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task WithoutTheHelpBridge_AMessageNamesNoHelpTopic()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("NoHelpAnn");
			(await Installer.UninstallAsync("watch-help", force: false, CancellationToken.None)).Expect<Success>();

			await Assert.That(await As(ann, "+watch/bogus")).Contains("+watch has no /bogus switch.").And.DoesNotContain("+help");
			await Assert.That(await As(ann, "+watch something")).Contains("+watch/add <name> watches someone.").And.DoesNotContain("+help");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task APlayerWatchedByAsManyAsFitIsRefusedMore()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("FullAnn");
			var bo = await Player("FullBo");
			var limit = int.Parse(await God("think config(max_attribute_value_length)"));
			// A watcher list that fills the attribute but for five bytes, too few for one more objid.
			await God($"&WATCH`{await Key(bo)}`BY {Store}={new string('x', limit - 5)}");

			await Assert.That(await As(ann, $"+watch/add {bo.Name}")).Contains($"has as many watchers as fit in {limit} bytes.");
			await Assert.That(await God($"think strlen(get({Store}/WATCH`{await Key(ann)}`LIST))")).IsEqualTo("0")
				.Because("a refused watch is not added to the watcher's own list");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task SettingsAreChangedThroughPlusConfig()
	{
		try
		{
			await InstallAsync();
			var staff = await Player("WsStaff");
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
			var ann = await Player("WsAnn");
			var bo = await Player("WsBo");

			await Assert.That(await As(staff, "+config watch")).Contains("max").And.Contains("quiet");
			await Assert.That(await As(staff, "+config watch/quiet=10m")).Contains("watch/quiet is now 10m, was 5m.");
			await As(staff, "+config/reset watch/quiet");

			// The most a player may watch is read from the setting.
			await Assert.That(await As(staff, "+config watch/max=1")).Contains("watch/max is now 1, was 100.");
			try
			{
				await Assert.That(await As(staff, $"+watch/add {ann.Name}")).Contains($"You watch {ann.Name} now");
				await Assert.That(await As(staff, $"+watch/add {bo.Name}")).Contains("You watch 1 people");
			}
			finally
			{
				await As(staff, "+config/reset watch/max");
			}
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AWatchListHearsConnectsAndLeavesWithinWhatThePlayerAllows()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("WaAnn");
			var bo = await Player("WaBo");
			var cy = await Player("WaCy");

			await Assert.That(await As(bo, "+watch")).Contains("You watch nobody.");
			await Assert.That(await As(bo, $"+watch/add {bo.Name}")).Contains("You cannot watch yourself.");
			await Assert.That(await As(bo, $"+watch/add {ann.Name}")).Contains($"You watch {ann.Name} now");
			await Assert.That(await As(bo, $"+watch/add {ann.Name}")).Contains($"You already watch {ann.Name}.");
			// +friend is the same command, the way Volund's FRIEND global spells it.
			await Assert.That(await As(cy, $"+friend/add {ann.Name}")).Contains($"You watch {ann.Name} now");
			await Assert.That(await As(bo, "+watch")).Contains("Watched and on").And.Contains(ann.Name).And.Contains("WaAnnRoom");
			// Nobody has a +who status, so there is no Status column of dashes.
			await Assert.That(await As(bo, "+watch")).DoesNotContain("Status");

			// +watch shows the status +who shows, whichever package gives it.
			var whoWhere = (await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("who-where"))
				.First(o => o.Ref == "ww_functions").Objid;
			var status = TestIsolationHelpers.GenerateUniqueName("WaStatus");
			await God($"&FILTER`WHO`STATUS`ZZ {whoWhere}=if(strmatch(%1,#{ann.DbRef.Number}),{status},%0)");
			await Assert.That(Unwrapped(await As(bo, "+watch"))).Contains(status).And.Contains("Status");
			await God($"&FILTER`WHO`STATUS`ZZ {whoWhere}");
			await Assert.That(await As(bo, "+watch/list")).Contains(ann.Name).And.Contains("on now");
			var watchers = await As(ann, "+watch/watchers");
			await Assert.That(watchers).Contains(bo.Name).And.Contains(cy.Name).And.Contains("2 watching");

			// Bo and Cy hear Ann leave only when her connect was announced; this one was not.
			var annKey = await Key(ann);
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).IsEmpty();
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).IsEquivalentTo(new[] { $"[WATCH] {ann.Name} has connected." });
			await Assert.That(Heard(cy, $"{ann.Name} has connected.")).Count().IsEqualTo(1);
			await Assert.That(await As(bo, "+watch/list")).Contains("on now");
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).Count().IsEqualTo(1);
			await Assert.That(await As(bo, "+watch/list")).Contains("ago");

			// A connect within the quiet setting's seconds of the last one announced is not announced, nor its leaving.
			ann = await Connect(ann);
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(1);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).Count().IsEqualTo(1);

			// Muted, Bo hears of nobody; Cy still does.
			await Assert.That(await As(bo, "+watch/mute")).Contains("You no longer hear when the people you watch connect and leave.");
			await God($"&WATCH`{annKey}`SAID {Store}=1");
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(1);
			await Assert.That(Heard(cy, $"{ann.Name} has connected.")).Count().IsEqualTo(2);
			await As(bo, "+watch/unmute");

			// Ann's page lock keeps Cy out.
			await As(ann, $"@lock/page me=!#{cy.DbRef.Number}");
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).Count().IsEqualTo(2);
			await Assert.That(Heard(cy, $"{ann.Name} has disconnected.")).Count().IsEqualTo(1);
			await God($"@unlock/page #{ann.DbRef.Number}");

			// Hidden from watchers, or DARK, Ann is not announced; nor is her leaving once she hides.
			await God($"&WATCH`{annKey}`SAID {Store}=1");
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(2);
			await Assert.That(await As(ann, "+watch/hide")).Contains("Nobody hears when you connect or leave");
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).Count().IsEqualTo(2);
			await God($"&WATCH`{annKey}`SAID {Store}=1");
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(2);
			await Assert.That(await As(bo, "+watch")).Contains("Nobody you watch is on.");
			await As(ann, "+watch/unhide");
			await Disconnect(ann);
			await God($"@set #{ann.DbRef.Number}=DARK");
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(2);
			await God($"@set #{ann.DbRef.Number}=!DARK");

			// At login, a player hears who of those they watch is on.
			await Disconnect(bo);
			bo = await Connect(bo);
			await Assert.That(Heard(bo, "On now of those you watch:")).IsEquivalentTo(new[] { $"[WATCH] On now of those you watch: {ann.Name}." });

			await Assert.That(await As(cy, $"+friend/delete {ann.Name}")).Contains($"You no longer watch {ann.Name}.");
			await Assert.That(await As(cy, $"+watch/del {ann.Name}")).Contains($"You do not watch {ann.Name}.");
			await Assert.That(await As(ann, "+watch/watchers")).Contains("1 watching").And.DoesNotContain(cy.Name);

			// A connect nobody hears does not count: the leaving after it is not announced, and the next connect is.
			await Disconnect(ann);
			await As(bo, "+watch/mute");
			await God($"&WATCH`{annKey}`SAID {Store}=1");
			ann = await Connect(ann);
			await As(bo, "+watch/unmute");
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).Count().IsEqualTo(2);
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(3);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>Large data: a player with two hundred watchers. The list pages, and a connect reaches every watcher once.</summary>
	[Test]
	public async Task ManyWatchersPageAndAnnounce()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("StAnn");
			var players = new List<TestIsolationHelpers.TestPlayer>();
			for (var count = 0; count < 200; count++)
			{
				players.Add(await Player("StW"));
			}
			// Two hundred watchers, written onto the store as +watch/add would.
			var annKey = await Key(ann);
			var objids = new List<string>();
			foreach (var watcher in players) objids.Add(await God($"think objid(#{watcher.DbRef.Number})"));
			await God($"&WATCH`{annKey} {Store}=objid(#{ann.DbRef.Number})");
			await God($"&WATCH`{annKey}`BY {Store}={string.Join(' ', objids)}");
			var watching = await As(ann, "+watch/watchers");
			await Assert.That(watching).Contains("page 1 of 4").Because("three names to a row, 18 rows to a page");
			await Assert.That(await As(ann, "+watch/watchers/4")).Contains("page 4 of 4");

			ann = await Connect(ann);
			foreach (var watcher in players)
			{
				await Assert.That(Heard(watcher, $"{ann.Name} has connected.")).Count().IsEqualTo(1);
			}
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
