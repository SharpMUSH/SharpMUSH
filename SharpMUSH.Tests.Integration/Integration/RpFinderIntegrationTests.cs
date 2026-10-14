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
/// The bundled rp-finder package, installed from the catalogue with who-where and driven with typed commands:
/// +lfrp statuses, shown in +who, and the watch list fed by the master room's ACONNECT and ADISCONNECT. Each
/// test installs both packages and removes them again. Nothing waits on the clock: a status that ran out, or a
/// connect outside the quiet window, is written onto the store. Not in parallel: +lfrp, +watch and +who are
/// global.
/// </summary>
[NotInParallel]
public class RpFinderIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private string Finder { get; set; } = string.Empty;
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

	/// <summary>The key a player's records are filed under on the store.</summary>
	private async Task<string> Key(TestIsolationHelpers.TestPlayer player)
		=> await God($"think edit(objid(#{player.DbRef.Number}),#,,:,_)");

	private async Task InstallAsync()
	{
		await ApplyAsync("who-where");
		await ApplyAsync("rp-finder");
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("rp-finder");
		Finder = objects.First(o => o.Ref == "finder").Objid;
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
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync()
	{
		await Installer.UninstallAsync("rp-finder", force: true, CancellationToken.None);
		await Installer.UninstallAsync("who-where", force: true, CancellationToken.None);
	}

	[Test]
	public async Task AStatusIsSetListedShownAndCleared()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("LfAnn");
			var bo = await Player("LfBo");
			var mod = await Player("LfMod", "rp-moderator");

			await Assert.That(await As(ann, "+lfrp/note Short scenes")).Contains("Say what you are looking for first");
			var line = TestIsolationHelpers.GenerateUniqueName("LfLine");
			await Assert.That(await As(ann, $"+lfrp {line}")).Contains($"You are looking for RP: {line}")
				.And.Contains("It lasts 4 hours or until you log off.");
			await Assert.That(await As(ann, $"+lfrp {new string('x', 101)}")).Contains("Keep it to 100 characters.");

			// What a player types is never evaluated.
			await Assert.That(await As(ann, "+lfrp/note Short scenes; no combat. [add(1,2)] 50% off"))
				.Contains("Your note: Short scenes; no combat. [add(1,2)] 50% off");
			await Assert.That(await As(ann, "+lfrp/place here")).Contains("Others see where you are standing");

			var list = await As(bo, "+lfrp");
			await Assert.That(list).Contains("Looking for RP").And.Contains(ann.Name).And.Contains(line)
				.And.Contains("LfAnnRoom");
			var shown = await As(bo, $"+lfrp/show {ann.Name}");
			await Assert.That(shown).Contains("Short scenes; no combat. [add(1,2)] 50% off").And.Contains("LfAnnRoom");
			await Assert.That(await As(bo, "+lfrp/show")).Contains("You are not looking for RP.");

			await Assert.That(await As(bo, $"+lfrp/off {ann.Name}")).Contains("Only an RP moderator can clear someone else's status.");
			await Assert.That(await As(mod, $"+lfrp/off {ann.Name}")).Contains($"{ann.Name} is no longer looking for RP.");
			await Assert.That(Heard(ann, "cleared your +lfrp status")).IsEquivalentTo(new[] { $"[RP] {mod.Name} cleared your +lfrp status." });
			await Assert.That(await As(bo, $"+lfrp/show {ann.Name}")).Contains($"{ann.Name} is not looking for RP.");

			// A status runs out DATA`LFRP`HOURS after it was last set, and a fresh one starts without the old note.
			await As(ann, $"+lfrp {line}");
			await As(ann, "+lfrp/note Old note");
			var annKey = await Key(ann);
			await God($"&LFRP`{annKey}`UNTIL {Store}=1");
			await Assert.That(await As(bo, $"+lfrp/show {ann.Name}")).Contains($"{ann.Name} is not looking for RP.");
			await As(bo, "+lfrp");
			await Assert.That(await God($"think lattr({Store}/LFRP`{annKey}*)")).IsEqualTo(string.Empty)
				.Because("listing clears statuses that ran out");
			await As(ann, $"+lfrp {line}");
			await Assert.That(await As(ann, "+lfrp/show")).Contains("Note:").And.DoesNotContain("Old note");

			await Assert.That(await As(ann, "+lfrp/off")).Contains("You are no longer looking for RP.");
			await Assert.That(await As(ann, "+lfrp/off")).Contains("You are not looking for RP.");

			// Logging off ends it.
			await As(ann, $"+lfrp {line}");
			await Disconnect(ann);
			await Assert.That(await God($"think lattr({Store}/LFRP`{annKey}*)")).IsEqualTo(string.Empty);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task SwitchesPagesAndHelpAreChecked()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("SwAnn");

			await Assert.That(await As(ann, "+lfrp/bogus")).Contains("+lfrp has no /bogus switch. +help lfrp lists them.");
			await Assert.That(await As(ann, "+watch/bogus")).Contains("+help watch lists them.");
			await Assert.That(await As(ann, "+watch/add/del x")).Contains("Use one switch at a time.");
			await Assert.That(await As(ann, "+lfrp/note/2 x")).Contains("+lfrp/note takes no page number.");
			await Assert.That(await As(ann, "+watch/list/0")).Contains("Pages start at 1");
			await Assert.That(await As(ann, "+watch something")).Contains("+watch/add <name> watches someone.");

			await Assert.That(await As(ann, "+help rp")).Contains("+lfrp").And.Contains("+watch");
			await Assert.That(await As(ann, "+help who filters")).Contains("FILTER`WHO`STATUS`<name>");
			await Assert.That(await As(ann, "+help watch")).Contains("+watch/watchers");
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
			await Assert.That(await As(bo, "+watch/list")).Contains(ann.Name).And.Contains("on now");
			var watchers = await As(ann, "+watch/watchers");
			await Assert.That(watchers).Contains(bo.Name).And.Contains(cy.Name).And.Contains("2 watching");

			// Bo and Cy hear Ann leave only when her connect was announced; this one was not.
			var annKey = await Key(ann);
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).IsEmpty();
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).IsEquivalentTo(new[] { $"[RP] {ann.Name} has connected." });
			await Assert.That(Heard(cy, $"{ann.Name} has connected.")).Count().IsEqualTo(1);
			await Assert.That(await As(bo, "+watch/list")).Contains("on now");
			await Disconnect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has disconnected.")).Count().IsEqualTo(1);
			await Assert.That(await As(bo, "+watch/list")).Contains("ago");

			// A connect within DATA`WATCH`QUIET seconds of the last one announced is not announced, nor its leaving.
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

			// Hidden from watchers, or DARK, Ann is not announced.
			ann = await Connect(ann);
			await Assert.That(await As(ann, "+watch/hide")).Contains("Nobody hears when you connect or leave");
			await Disconnect(ann);
			await God($"&WATCH`{annKey}`SAID {Store}=1");
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(1);
			await Assert.That(await As(bo, "+watch")).Contains("Nobody you watch is on.");
			await As(ann, "+watch/unhide");
			await Disconnect(ann);
			await God($"@set #{ann.DbRef.Number}=DARK");
			ann = await Connect(ann);
			await Assert.That(Heard(bo, $"{ann.Name} has connected.")).Count().IsEqualTo(1);
			await God($"@set #{ann.DbRef.Number}=!DARK");

			// At login, a player hears who of those they watch is on.
			await Disconnect(bo);
			bo = await Connect(bo);
			await Assert.That(Heard(bo, "On now of those you watch:")).IsEquivalentTo(new[] { $"[RP] On now of those you watch: {ann.Name}." });

			await Assert.That(await As(cy, $"+friend/delete {ann.Name}")).Contains($"You no longer watch {ann.Name}.");
			await Assert.That(await As(cy, $"+watch/del {ann.Name}")).Contains($"You do not watch {ann.Name}.");
			await Assert.That(await As(ann, "+watch/watchers")).Contains("1 watching").And.DoesNotContain(cy.Name);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AStatusShowsInWho()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("WhAnn");
			var bo = await Player("WhBo");

			await Assert.That(await As(bo, "+who")).DoesNotContain("Status").Because("nobody has a status yet");
			var line = TestIsolationHelpers.GenerateUniqueName("WhLine");
			await As(ann, $"+lfrp {line}");
			var who = await As(bo, "+who");
			await Assert.That(who).Contains("Status").And.Contains(line);

			// A long line is cut to 40 characters.
			var whoWhere = (await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("who-where"))
				.First(o => o.Ref == "ww_functions").Objid;
			await As(ann, $"+lfrp {line} {new string('x', 60)}");
			await Assert.That(await God($"think u({whoWhere}/WW_FN_FILTER,WHO`STATUS,,#{ann.DbRef.Number})"))
				.IsEqualTo($"{line} {new string('x', 37 - line.Length - 1)}...");
			await As(ann, $"+lfrp {line}");

			// Another package's filter runs after this one, in name order, and gets its status as %0.
			await God($"&FILTER`WHO`STATUS`ZZ {whoWhere}=if(strlen(%0),%[%0%],%0)");
			await Assert.That(await As(bo, "+who")).Contains($"[{line}");
			await God($"&FILTER`WHO`STATUS`ZZ {whoWhere}");

			// A status that ran out is gone from +who.
			await God($"&LFRP`{await Key(ann)}`UNTIL {Store}=1");
			await Assert.That(await As(bo, "+who")).DoesNotContain(line);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>
	/// Large data: two hundred players looking for RP, and a player with two hundred watchers. Each list pages,
	/// and a connect reaches every watcher once.
	/// </summary>
	[Test]
	public async Task ManyStatusesAndWatchersPageAndAnnounce()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("StAnn");
			var bo = await Player("StBo");
			var players = new List<TestIsolationHelpers.TestPlayer>();
			for (var count = 0; count < 200; count++)
			{
				players.Add(await Player("StW"));
			}
			foreach (var player in players)
			{
				await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"+lfrp Stress status of {player.Name}"));
			}
			await WebAppFactoryArg.QueueBarrierAsync();

			var first = await As(bo, "+lfrp");
			await Assert.That(first).Contains("Looking for RP").And.Contains("page 1 of").And.Contains("+lfrp/2");
			var pages = int.Parse(System.Text.RegularExpressions.Regex.Match(first, @"page 1 of (\d+)").Groups[1].Value);
			await Assert.That(await As(bo, $"+lfrp/{pages}")).Contains($"page {pages} of {pages}");
			await Assert.That(await As(bo, $"+lfrp/{pages + 1}")).Contains($"There are only {pages} pages.");
			await Assert.That(await As(bo, "+who")).Contains("Status");

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
