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
/// The bundled status package, installed from the catalogue with who-where and driven with typed commands:
/// +status, shown in +who. Each test installs status and removes it again. Nothing waits on the clock: a status
/// that ran out is written onto the store. Not in parallel: +status and +who are global.
/// </summary>
[NotInParallel]
public class StatusIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private string StatusObject { get; set; } = string.Empty;
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

	/// <summary>Installs who-where and status; each test removes both.</summary>
	private async Task InstallAsync()
	{
		await ApplyAsync("who-where");
		await ApplyAsync("status");
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("status");
		StatusObject = objects.First(o => o.Ref == "status").Objid;
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
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync()
	{
		await Installer.UninstallAsync("status", force: true, CancellationToken.None);
		await Installer.UninstallAsync("who-where", force: true, CancellationToken.None);
	}

	[Test]
	public async Task AStatusIsSetListedShownAndCleared()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("StaAnn");
			var bo = await Player("StaBo");
			var mod = await Player("StaMod", "status-moderator");

			var choices = await As(ann, "+status");
			await Assert.That(choices).Contains("You have no status.").And.Contains("lfrp").And.Contains("rp_ok")
				.And.Contains("Not looking, but glad to be asked.").And.Contains("4h or logoff").And.Contains("until changed");
			await Assert.That(await As(ann, "+status/note Short scenes")).Contains("Choose a status first");
			await Assert.That(await As(ann, "+status bogus")).Contains("There is no status 'bogus'.");
			await Assert.That(await As(ann, "+status lfrp`tag")).Contains("There is no status 'lfrp`tag'.");

			var line = TestIsolationHelpers.GenerateUniqueName("StaLine");
			await Assert.That(await As(ann, $"+status LFRP={line}")).Contains($"Your status: LFRP: {line}")
				.And.Contains("It lasts 4 hours or until you log off.");
			await Assert.That(await As(ann, $"+status lfrp={new string('x', 101)}")).Contains("Keep it to 100 characters.");

			// What a player types is never evaluated.
			await Assert.That(await As(ann, "+status/note Short scenes; no combat. [add(1,2)] 50% off"))
				.Contains("Your note: Short scenes; no combat. [add(1,2)] 50% off");
			await Assert.That(await As(ann, "+status/place here")).Contains("Others see where you are standing");

			var list = await As(bo, "+status/list");
			await Assert.That(list).Contains("Statuses").And.Contains(ann.Name).And.Contains(line).And.Contains("StaAnnRoom");
			await Assert.That(await As(bo, "+status/list lfrp")).Contains(ann.Name);
			await Assert.That(await As(bo, "+status/list rp_ok")).Contains("Nobody has the rp_ok status right now.");
			await Assert.That(await As(bo, "+status/list nope")).Contains("There is no status 'nope'.");
			var shown = await As(bo, $"+status/show {ann.Name}");
			await Assert.That(shown).Contains($"LFRP: {line}").And.Contains("Short scenes; no combat. [add(1,2)] 50% off").And.Contains("StaAnnRoom");
			await Assert.That(await As(bo, "+status/show")).Contains("You have no status.");

			// Choosing another status starts it fresh: the note and place go.
			await Assert.That(await As(ann, "+status rp_ok")).Contains("Your status: RP OK").And.Contains("It lasts until you change it.");
			await Assert.That(await As(ann, "+status")).Contains("RP OK").And.DoesNotContain("Short scenes");
			await Assert.That(await As(bo, "+status/list rp_ok")).Contains(ann.Name);

			await Assert.That(await As(bo, $"+status/off {ann.Name}")).Contains("Only a status moderator can clear someone else's status.");
			await Assert.That(await As(mod, $"+status/off {ann.Name}")).Contains($"{ann.Name}'s status is cleared.");
			await Assert.That(Heard(ann, "cleared your status")).IsEquivalentTo(new[] { $"[STATUS] {mod.Name} cleared your status." });
			await Assert.That(await As(bo, $"+status/show {ann.Name}")).Contains($"{ann.Name} has no status.");

			// LFRP runs out its hours after it was last set, and a fresh one starts without the old note.
			await As(ann, $"+status lfrp={line}");
			await As(ann, "+status/note Old note");
			var annKey = await Key(ann);
			await God($"&PLAYER`{annKey}`UNTIL {Store}=1");
			await Assert.That(await As(bo, $"+status/show {ann.Name}")).Contains($"{ann.Name} has no status.");
			await As(bo, "+status/list");
			await Assert.That(await God($"think lattr({Store}/PLAYER`{annKey}*)")).IsEqualTo(string.Empty)
				.Because("listing clears statuses that ran out");
			await As(ann, $"+status lfrp={line}");
			await Assert.That(await As(ann, "+status/show")).Contains("Note:").And.DoesNotContain("Old note");

			await Assert.That(await As(ann, "+status/off")).Contains("Your status is cleared.");
			await Assert.That(await As(ann, "+status/off")).Contains("You have no status.");

			// Logging off ends LFRP, but not RP_OK.
			await As(ann, $"+status lfrp={line}");
			await Disconnect(ann);
			await Assert.That(await God($"think lattr({Store}/PLAYER`{annKey}*)")).IsEqualTo(string.Empty);
			await As(bo, "+status rp_ok");
			await Disconnect(bo);
			await Assert.That(await God($"think get({Store}/PLAYER`{await Key(bo)}`NAME)")).IsEqualTo("RP_OK");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AGameAddsAndRemovesStatuses()
	{
		try
		{
			await InstallAsync();
			var ann = await Player("GaAnn");
			var bo = await Player("GaBo");
			var admin = await Player("GaAdm");
			await God($"@permission/allow #{admin.DbRef.Number}=config.admin");
			// A short room name keeps Location to one line, so only the Status cell wraps and Unwrapped reads it whole.
			await God($"@name {await God($"think loc(#{ann.DbRef.Number})")}=GaR");

			// Staff who change the game's configuration change the statuses on offer, a +config table.
			await Assert.That(await As(ann, "+config/add status/statuses/scene=x")).Contains("Changing status/statuses needs the config.admin permission.");
			await Assert.That(await As(ann, "+config status/statuses/lfrp/lasts=1h")).Contains("Changing status/statuses needs the config.admin permission.");
			await Assert.That(await As(admin, "+config/add status/statuses/scene=In a scene, but come and join.")).Contains("Added status/statuses/scene.");
			await Assert.That(await As(admin, "+config/add status/statuses/scene=again")).Contains("status/statuses already has scene.");
			await Assert.That(await As(admin, "+config/add status/statuses/two words=x")).Contains("An entry's name is letters, digits, - and _.");
			await Assert.That(await As(admin, $"+config/add status/statuses/{new string('a', 21)}=x")).Contains("An entry's name must match");
			await Assert.That(await As(admin, "+config/add status/statuses/quiet")).Contains("+config/add status/statuses/quiet=<means>");
			await Assert.That(await As(admin, "+config status/statuses/scene/tag=Open scene")).Contains("status/statuses/scene/tag is now Open scene, was none.");
			await Assert.That(await As(admin, "+config status/statuses/scene/lasts=1h")).Contains("status/statuses/scene/lasts is now 1h, was 0s.");
			await Assert.That(await As(admin, "+config status/statuses/scene/lasts=x")).Contains("Give a length of time");
			await Assert.That(await As(admin, "+config status/statuses/scene/colour=red")).Contains("Its fields are means, tag, lasts, and logoff.");
			await Assert.That(await As(admin, "+config status/statuses/scene/logoff=yes")).Contains("status/statuses/scene/logoff is now yes, was no.");
			await Assert.That(await As(ann, "+status")).Contains("1h or logoff");
			await As(admin, "+config/reset status/statuses/scene/logoff");
			await Assert.That(await God($"think get({Store}/STATUS`SCENE`LASTS):[get({Store}/STATUS`SCENE`LOGOFF)]")).IsEqualTo("3600:0");
			await Assert.That(await As(bo, "+config status/statuses")).Contains("scene").And.Contains("Tag: Open scene");
			await Assert.That(await As(bo, "+config status/statuses/scene")).Contains("Text, from 1 to 200").And.Contains("Ends at logoff");
			await Assert.That(await As(ann, "+status")).Contains("scene").And.Contains("In a scene, but come and join.").And.Contains("1h");
			await Assert.That(await As(ann, "+status scene=The tavern")).Contains("Your status: Open scene: The tavern")
				.And.Contains("It lasts 1 hour.");
			await Assert.That(Unwrapped(await As(bo, "+who"))).Contains("Openscene:Thetavern");

			// The list-footer setting goes under the list, and the line length is a +config setting too.
			await Assert.That(await As(admin, "+config status/list-footer=Longer requests: +bbread public/looking"))
				.Contains("status/list-footer is now Longer requests: +bbread public/looking, was none.");
			await Assert.That(await As(bo, "+status/list scene")).Contains("Open scene").And.Contains("Longer requests: +bbread public/looking");
			await As(admin, "+config/reset status/list-footer");
			await Assert.That(await As(bo, "+status/list scene")).DoesNotContain("Longer requests");
			await Assert.That(await As(admin, "+config status/line-length=500")).Contains("It must be at most 400.");
			await As(admin, "+config status/line-length=20");
			await Assert.That(await As(ann, $"+status scene={new string('x', 21)}")).Contains("Keep it to 20 characters.");
			await As(admin, "+config/reset status/line-length");

			// Removing a status takes it from everyone who had it.
			var removed = await As(admin, "+config/remove status/statuses/scene");
			await Assert.That(removed).Contains("Removed status/statuses/scene.").And.Contains("Cleared scene from 1 player.");
			await Assert.That(await God($"think lattr({Store}/STATUS`SCENE*)")).IsEqualTo(string.Empty);
			await Assert.That(await As(ann, "+status")).Contains("You have no status.").And.DoesNotContain("Open scene");
			await Assert.That(Unwrapped(await As(bo, "+who"))).DoesNotContain("Openscene");
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
			var ann = await Player("SwsAnn");

			await Assert.That(await As(ann, "+status/bogus")).Contains("+status has no /bogus switch. +help status lists them.");
			await Assert.That(await As(ann, "+status/note/off x")).Contains("Use one switch at a time.");
			await Assert.That(await As(ann, "+status/note/2 x")).Contains("+status/note takes no page number.");
			await Assert.That(await As(ann, "+status/list/0")).Contains("Pages start at 1");

			await Assert.That(await As(ann, "+help status")).Contains("+status lfrp=").And.Contains("+status/list");
			await Assert.That(await As(ann, "+help status staff")).Contains("+config status/statuses/<name>/lasts=<time>");
			await Assert.That(await As(ann, "+status/options lfrp/hours=1")).Contains("+status has no /options switch.");
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
			var ann = await Player("WhsAnn");
			var bo = await Player("WhsBo");
			// A short room name keeps Location to one line, so only the Status cell wraps and Unwrapped reads it whole.
			await God($"@name {await God($"think loc(#{ann.DbRef.Number})")}=WhsR");

			var line = TestIsolationHelpers.GenerateUniqueName("WhsLine");
			await As(ann, $"+status lfrp={line}");
			var who = await As(bo, "+who");
			await Assert.That(who).Contains("Status");
			await Assert.That(Unwrapped(who)).Contains($"LFRP:{line}");

			// A long line is cut to 40 characters, tag and all.
			var whoWhere = (await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("who-where"))
				.First(o => o.Ref == "ww_functions").Objid;
			await As(ann, $"+status lfrp={line} {new string('x', 60)}");
			await Assert.That(await God($"think u({whoWhere}/FUN`FILTER`APPLY,WHO`STATUS,,#{ann.DbRef.Number})"))
				.IsEqualTo($"LFRP: {line} {new string('x', 37 - line.Length - 7)}...");
			await As(ann, "+status rp_ok");
			await Assert.That(await God($"think u({whoWhere}/FUN`FILTER`APPLY,WHO`STATUS,,#{ann.DbRef.Number})")).IsEqualTo("RP OK");

			// Another package's filter runs after this one, in name order, and gets its status as %0.
			await God($"&FILTER`WHO`STATUS`ZZ {whoWhere}=if(strlen(%0),%[%0%],%0)");
			await Assert.That(await God($"think u({whoWhere}/FUN`FILTER`APPLY,WHO`STATUS,,#{ann.DbRef.Number})")).IsEqualTo("[RP OK]");
			await God($"&FILTER`WHO`STATUS`ZZ {whoWhere}");

			// A status that ran out is gone from +who.
			await As(ann, $"+status lfrp={line}");
			await God($"&PLAYER`{await Key(ann)}`UNTIL {Store}=1");
			await Assert.That(Unwrapped(await As(bo, "+who"))).DoesNotContain(line);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	/// <summary>Large data: two hundred players with a status. The list pages, and +who still draws.</summary>
	[Test]
	public async Task ManyStatusesPage()
	{
		try
		{
			await InstallAsync();
			var bo = await Player("StsBo");
			var players = new List<TestIsolationHelpers.TestPlayer>();
			for (var count = 0; count < 200; count++)
			{
				players.Add(await Player("StsW"));
			}
			foreach (var player in players)
			{
				await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"+status lfrp=Stress status of {player.Name}"));
			}
			await WebAppFactoryArg.QueueBarrierAsync();

			var first = await As(bo, "+status/list lfrp");
			await Assert.That(first).Contains("LFRP").And.Contains("page 1 of").And.Contains("+status/list/2 lfrp");
			var pages = int.Parse(System.Text.RegularExpressions.Regex.Match(first, @"page 1 of (\d+)").Groups[1].Value);
			await Assert.That(await As(bo, $"+status/list/{pages}")).Contains($"page {pages} of {pages}");
			await Assert.That(await As(bo, $"+status/list/{pages + 1}")).Contains($"There are only {pages} pages.");
			await Assert.That(await As(bo, "+who")).Contains("Status");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
