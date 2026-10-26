using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The discord, discord-channels and discord-scenes packages, driven the way a game drives them. The test host
/// runs no Discord plugin, so each is installed from its bundled manifest with the discord plugin left out of
/// <c>plugins:</c>: the softcode is what is tested here, and the plugin only posts what the feed and
/// <c>@discord/event</c> are handed. A line Discord members posted is sent onto the feed with its
/// <c>discord/&lt;message id&gt;</c> origin, as the plugin sends it, and <c>@discord</c> is a command added for the
/// test whose hook writes down every call. Not in parallel: the packages attach to the one event handler.
/// </summary>
[NotInParallel]
public class DiscordPackagesIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();

	private async Task<string> God(string command)
	{
		var said = (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message?.ToPlainText()?.Trim() ?? string.Empty;
		await WebAppFactoryArg.QueueBarrierAsync();
		return said;
	}

	private TestIsolationHelpers.TestPlayer _staff = null!;

	/// <summary>A wizard in a room of their own, who runs the test's commands.</summary>
	private async Task StaffAsync()
	{
		var god = (await Mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(new DBRef(1))))
			.Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("DiscordRoom"), god));
		_staff = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DiscordStaff", home);
		await God($"@set #{_staff.DbRef.Number}=WIZARD");
	}

	/// <summary>What the staff member was told while <paramref name="command"/> ran.</summary>
	private async Task<string> AsStaff(string command)
	{
		var before = Notifications.CountFor(_staff.DbRef);
		await Parser.CommandParse(_staff.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(_staff.DbRef).Skip(before));
	}

	private Task<string> Think(string expression) => God($"think {expression}");

	private async Task<string> PackageObjectAsync(string package, string reference) =>
		$"#{DBRef.Parse((await Registry.GetPackageObjectsAsync(package)).First(o => o.Ref == reference).Objid).Number}";

	/// <summary>Installs <paramref name="package"/> from its bundled manifest, without the discord plugin it names.</summary>
	private async Task ApplyAsync(string package)
	{
		var yaml = BundledPackages.ManifestYaml(package)
			.Replace("plugins: [discord]\n", string.Empty)
			.Replace("plugins: [scene, discord]", "plugins: [scene]");
		var manifest = WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>().ParseManifest(yaml) switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(
				string.Join("; ", failure.Issues.Select(i => i.ToString())))
		};
		var applied = await Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource(BundledPackages.SourceRepo, package, BundledPackages.SourceCommit, null),
			new Dictionary<string, string>(), []));
		await Assert.That(applied is PackageApplyResult).IsTrue()
			.Because($"{package} must install: {(applied is Error<string> error ? error.Value : string.Empty)}");
		// AINSTALL is queued after the apply.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task UninstallAsync(params string[] packages)
	{
		foreach (var package in packages)
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync(package);
		}
	}

	/// <summary>A Discord channel id no other test uses: 18 digits.</summary>
	private static string DiscordChannelId() => $"14270{Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999)}";

	/// <summary>The lines the Discord plugin would have been handed for feed <paramref name="feed"/>.</summary>
	private List<FeedLine> Posted(string feed) =>
		WebAppFactoryArg.Services.GetRequiredService<RecordingFeedLineListener>().Lines
			.Where(line => line.Message.Feed == feed).ToList();

	/// <summary>Sends <paramref name="text"/> on <paramref name="feed"/> as a Discord member's post, from God.</summary>
	private async Task PostFromDiscordAsync(string feed, string text)
	{
		var feeds = WebAppFactoryArg.Services.GetRequiredService<IFeedService>();
		var kind = (await feeds.GetKindAsync("discord")).Expect<SharpFeedKind>();
		var god = (await Mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		await feeds.SendAsync(Parser.FromState(ParserState.RootFor(new DBRef(1))),
			new FeedSend(kind, SharpFeed.New("discord", feed.Split('/')[1]), god, god, FeedStyles.Say, MarkupText.Plain(text), [],
				Origin: $"discord/{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"));
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	[Test]
	public async Task AGameChannelIsLinkedListedJoinedBothWaysAndRemoved()
	{
		var channel = $"Dc{Random.Shared.Next(100000, 999999)}";
		var discord = DiscordChannelId();
		try
		{
			await StaffAsync();
			await ApplyAsync("discord");
			await ApplyAsync("discord-channels");
			await Assert.That(await AsStaff($"@channel/add {channel}=player")).Contains("Channel has been created.");
			await AsStaff($"@channel/on {channel}");

			await Assert.That(await AsStaff($"+discord/channel {channel}=12ab")).Contains("12ab is not a Discord channel id.");
			await Assert.That(await AsStaff($"+discord/channel NoSuch{channel}={discord}")).Contains($"There is no channel called NoSuch{channel}.");
			await Assert.That(await AsStaff($"+discord/channel {channel}={discord}"))
				.Contains($"{channel} goes to Discord channel {discord}, and what is posted there comes back.");
			await Assert.That(await AsStaff($"+discord/channel {channel}={discord}")).Contains($"That Discord channel already goes to {channel}.");

			var listed = await AsStaff("+discord/channels");
			await Assert.That(listed).Contains("Discord Channels");
			await Assert.That(listed).Contains(channel);
			await Assert.That(listed).Contains(discord);

			// A game channel's lines go to its Discord feed: said, posed and emitted.
			var said = TestIsolationHelpers.GenerateUniqueName("DcSaid");
			await AsStaff($"@chat {channel}={said}");
			var posed = TestIsolationHelpers.GenerateUniqueName("DcPosed");
			await AsStaff($"@chat {channel}=:{posed}");
			var emitted = TestIsolationHelpers.GenerateUniqueName("DcEmitted");
			await AsStaff($"@cemit {channel}={emitted}");
			for (var tries = 0; tries < 50 && !Posted($"discord/{discord}").Any(l => l.Message.Text.ToPlainText().Contains(emitted)); tries++)
			{
				await Task.Delay(100);
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			var lines = Posted($"discord/{discord}");
			await Assert.That(lines.Single(l => l.Message.Text.ToPlainText() == said).Message.Style).IsEqualTo("say");
			await Assert.That(lines.Single(l => l.Message.Text.ToPlainText() == posed).Message.Style).IsEqualTo("pose");
			await Assert.That(lines.Any(l => l.Message.Text.ToPlainText().Contains(emitted))).IsTrue()
				.Because(string.Join(" | ", lines.Select(l => $"{l.Message.Style}:{l.Message.DisplayName}:{l.Message.Text.ToPlainText()}")));

			// A line from Discord comes back onto the game channel, and does not go round again.
			var fromDiscord = TestIsolationHelpers.GenerateUniqueName("DcBack");
			var before = Posted($"discord/{discord}").Count;
			await PostFromDiscordAsync($"discord/{discord}", fromDiscord);
			await Notifications.WaitForAsync(_staff.DbRef, fromDiscord);
			await Assert.That(Notifications.For(_staff.DbRef).Any(l => l.Contains(fromDiscord) && l.Contains("says"))).IsTrue();
			await Assert.That(Posted($"discord/{discord}").Count).IsEqualTo(before + 1).Because("only the Discord post itself");

			await Assert.That(await AsStaff($"+discord/channel/remove {channel}")).Contains($"{channel} no longer goes to Discord channel {discord}.");
			await Assert.That(await AsStaff($"+discord/channel/remove {channel}")).Contains($"{channel} does not go to Discord.");
			await Assert.That(await AsStaff("+discord/channels")).DoesNotContain(discord);
		}
		finally
		{
			await God($"@channel/delete {channel}");
			await UninstallAsync("discord-channels", "discord");
		}
	}

	/// <summary>
	/// Waits out a scene change's chain to @discord: the event handler's attribute, the publisher's @trigger and the
	/// hooked command are each a queue entry queued by the one before, which one barrier does not cover.
	/// </summary>
	private Task SettleAsync() =>
		WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>().SettleForTestsAsync(rounds: 5, timeout: TimeSpan.FromSeconds(60));

	/// <summary>
	/// What @discord/event is asked for as a scene moves through its life, and the event it is handed. @discord is a
	/// command the test adds, hooked to a $-command that writes each call onto the hook's object.
	/// </summary>
	[Test]
	public async Task AScheduledScenesDiscordEventFollowsTheScene()
	{
		var hook = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "DiscordHook");
		var title = TestIsolationHelpers.GenerateUniqueName("DcScene");
		try
		{
			await StaffAsync();
			await ApplyAsync("discord");
			await ApplyAsync("discord-scenes");
			var publisher = await PackageObjectAsync("discord-scenes", "publisher");
			await God($"&CALL {hook}=$^@discord(/\\S+)? (.*)$:&LOG {hook}=[v(LOG)]%r%1 %2");
			await God($"@set {hook}/CALL=regexp");
			await God($"@set {hook}=!no_command");
			await God("@command/add @discord");
			await God($"@hook/override @discord={hook},CALL");

			var at = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
			var scheduled = await AsStaff($"+scene/schedule {at}/{title}=A pitch");
			var id = scheduled.Split("Scheduled scene ")[1].Split(' ')[0];

			await Assert.That(await Think($"u({publisher}/FUN`SCENE`READ,{id})%q<scene_status> %q<scene_waiting> %q<scene_listed>"))
				.IsEqualTo("scheduled 1 1");
			var json = await Think($"u({publisher}/FUN`SCENE`READ,{id})[u({publisher}/FUN`EVENT`JSON,{id})]");
			await Assert.That(json).Contains($"\"name\":\"{title}\"");
			await Assert.That(json).Contains($"\"start\":{at}");
			await Assert.That(json).Contains($"\"end\":{at + 10800}");
			await Assert.That(json).Contains($"\"location\":\"{await Think("mudname()")}: the game\"");

			// The game's web address joins /scenes/<id> with one slash, however mud_url is written.
			await Assert.That(await Think($"u({publisher}/FUN`EVENT`SITE,https://game.example/)")).IsEqualTo("https://game.example");
			await Assert.That(await Think($"u({publisher}/FUN`EVENT`SITE,https://game.example)")).IsEqualTo("https://game.example");

			var log = await Think($"get({hook}/LOG)");
			// The added command does not evaluate what it is handed, so the hook sees the call as written.
			await Assert.That(log).Contains($"/event scene-{id}=u(");

			await God($"&LOG {hook}");
			await AsStaff($"+scene/cancel {id}");
			await SettleAsync();
			await Assert.That(await Think($"get({hook}/LOG)")).Contains($"/event/end scene-{id}");

			// Publishing off: nothing goes up. Taking events down still runs.
			await God($"&DATA`CONFIG`PUBLISH {publisher}=0");
			await God($"&LOG {hook}");
			var second = await AsStaff($"+scene/schedule {at}/{title}x=A pitch");
			var secondId = second.Split("Scheduled scene ")[1].Split(' ')[0];
			await SettleAsync();
			await Assert.That(await Think($"get({hook}/LOG)")).DoesNotContain($"/event scene-{secondId}=");
			await AsStaff($"+scene/cancel {secondId}");
		}
		finally
		{
			await WebAppFactoryArg.Services.GetRequiredService<IHookService>().ClearHookAsync("@DISCORD", "OVERRIDE");
			await God("@command/delete @discord");
			await UninstallAsync("discord-scenes", "discord");
		}
	}
}
