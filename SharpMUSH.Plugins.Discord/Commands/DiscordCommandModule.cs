using System.Collections.Immutable;
using System.Globalization;
using Mediator;
using MarkupString;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;
using SharpMUSH.Plugins.Discord.Outbox;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Plugins.Discord.Commands;

/// <summary>
/// <c>@discord</c>: the bot's connection and the linked Discord channels. With no switch it shows both;
/// <c>/link &lt;channel id&gt;</c> links a channel to the feed <c>discord/&lt;channel id&gt;</c>, <c>/unlink</c> drops a link
/// and deletes its webhook, <c>/resume</c> starts a stopped link again, and <c>/connect</c> logs the bot in again after
/// its token or intents change. <c>/gate</c> lets only linked members post in a linked channel again, as linking does.
/// These need <c>discord.admin</c>. <c>/speak &lt;channel id&gt;</c>, run by any character on a linked account, makes it the
/// character its Discord user speaks as there; <c>/speak/off</c> stops that. <c>/bans on|off</c> mirrors game bans to Discord bans.
/// <c>/event</c> publishes Discord Scheduled Events (<see cref="DiscordEventCommands"/>).
/// Linking, unlinking, resuming and gating return the channel id at once and tell the admin how it went when it is done.
/// </summary>
public static class DiscordCommandModule
{
	private const string Badge = "DISCORD";
	private const string PermissionDenied = "#-1 PERMISSION DENIED";
	private const string NoSuchLink = "#-1 NO SUCH LINK";
	private const string BadChannel = "#-1 INVALID CHANNEL ID";

	/// <summary>How long a link or unlink waits on Discord before the admin is told it did not answer.</summary>
	private static readonly TimeSpan DiscordTimeout = TimeSpan.FromSeconds(60);

	[SharpCommand(Name = "@DISCORD", Switches = ["LINK", "UNLINK", "RESUME", "CONNECT", "GATE", "UNRESTRICTED", "RESTRICT", "SPEAK", "OFF", "MAP", "UNMAP",
			"CONFIRM", "ENFORCE", "REPORT", "DRIFT", "APPLY", "BANS", "EVENT", "START", "END"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGuest | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["target", "role"])]
	public static async ValueTask<Option<CallState>> Discord(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var services = parser.ServiceProvider;
		var notify = services.GetRequiredService<INotifyService>();
		var executor = await parser.CurrentState.KnownExecutorObject(services.GetRequiredService<IMediator>());
		var switches = parser.CurrentState.Switches.ToArray();
		var argument = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText().Trim() ?? "";
		if (switches.Contains("SPEAK"))
		{
			return await SpeakAsync(parser, executor, argument, off: switches.Contains("OFF"));
		}

		if (!await executor.Can(DiscordPlugin.AdminPermission))
		{
			await notify.Notify(executor, Notice(PermissionRefusal.Needs(DiscordPlugin.AdminPermission).Text, NoticeKind.Error));
			return new CallState(PermissionDenied);
		}

		if (switches.Contains("MAP") || switches.Contains("UNMAP") || switches.Contains("DRIFT"))
		{
			var right = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message?.ToPlainText().Trim();
			return await DiscordRoleCommands.RunAsync(parser, executor, switches, argument, right);
		}

		if (switches.Contains("EVENT"))
		{
			var right = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message?.ToPlainText().Trim();
			return await DiscordEventCommands.RunAsync(parser, executor, switches, argument, right);
		}

		if (switches.Contains("BANS"))
		{
			return await BansAsync(services, notify, executor, argument);
		}

		var relay = services.GetRequiredService<DiscordRelay>();
		var gateway = services.GetRequiredService<DiscordGatewayService>();
		var mirrorBans = services.GetRequiredService<IDiscordSettingsStore>().Get().MirrorBans;
		if (switches.Contains("CONNECT"))
		{
			gateway.Reconnect();
			await notify.Notify(executor, Notice("Connecting to Discord. @discord shows how it went.", NoticeKind.Ok));
			return CallState.Empty;
		}

		var gate = services.GetRequiredService<IDiscordChannelGate>();
		var action = switches.FirstOrDefault(name => name is "LINK" or "UNLINK" or "RESUME" or "GATE" or "UNRESTRICTED" or "RESTRICT");
		if (action is null)
		{
			var state = gateway.State;
			await notify.Notify(executor, Overview(parser.HouseLook(), state, mirrorBans, relay.Links(), gate));
			// Outside the panel, so it copies whole.
			if (state.ApplicationId is { } applicationId)
			{
				await notify.Notify(executor, MarkupText.Plain($"Add the bot to a server: {DiscordSetup.InviteUrl(applicationId, mirrorBans)}"));
			}

			return CallState.Empty;
		}

		if (!ulong.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var channelId))
		{
			await notify.Notify(executor, Notice(
				$"@discord/{action.ToLowerInvariant()} takes a channel's id. In Discord, turn on Developer Mode, then right-click the channel and Copy Channel ID.",
				NoticeKind.Warn));
			return new CallState(BadChannel);
		}

		// Linking and unlinking wait on Discord, and resuming waits behind them; the game's queue must not, so the answer
		// follows when it comes.
		var feeds = services.GetRequiredService<IFeedService>();
		var logger = services.GetRequiredService<ILogger<DiscordRelay>>();
		// The answer is told outside this command's queue entry, so none of the entry's ambient state (its output
		// lane among it, closed by then) may flow into the task.
		using (ExecutionContext.SuppressFlow())
		{
			_ = Task.Run(() => AnswerAsync(notify, executor, logger, cancellationToken => action switch
			{
				"LINK" => LinkAsync(relay, feeds, gate, executor, channelId, cancellationToken),
				"UNLINK" => UnlinkAsync(relay, channelId, cancellationToken),
				"GATE" or "RESTRICT" => AccessAsync(relay, gate, channelId, false, cancellationToken),
				"UNRESTRICTED" => AccessAsync(relay, gate, channelId, true, cancellationToken),
				_ => ResumeAsync(relay, channelId, cancellationToken),
			}));
		}

		return new CallState(channelId.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>Tells the admin how a link or unlink went. Runs off the game's queue, so nothing it throws reaches a caller.</summary>
	internal static async Task AnswerAsync(INotifyService notify, AnySharpObject executor, ILogger logger,
		Func<CancellationToken, ValueTask<MString>> work)
	{
		using var timeout = new CancellationTokenSource(DiscordTimeout);
		MString answer;
		try
		{
			answer = await work(timeout.Token);
		}
		catch (OperationCanceledException) when (timeout.IsCancellationRequested)
		{
			answer = Notice("Discord did not answer in time. @discord shows where the link stands.", NoticeKind.Error);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "[Discord] @discord failed.");
			answer = Notice("That failed; the server log says why.", NoticeKind.Error);
		}

		await notify.Notify(executor, answer);
	}

	private static async ValueTask<MString> LinkAsync(DiscordRelay relay, IFeedService feeds, IDiscordChannelGate gate,
		AnySharpObject executor, ulong channelId, CancellationToken cancellationToken)
	{
		var objid = executor.Object().DBRef.ToString();
		return await relay.LinkAsync(channelId, objid, new SecretAuthor(objid, executor.Object().Name), cancellationToken) switch
		{
			DiscordChannelLink link => await LinkedAsync(feeds, link, await gate.GateAsync(channelId, cancellationToken)),
			Error<string> error => Notice(error.Value, NoticeKind.Error),
		};
	}

	private static async ValueTask<MString> AccessAsync(DiscordRelay relay, IDiscordChannelGate gate, ulong channelId, bool unrestricted,
		CancellationToken cancellationToken)
	{
		if (relay.Links().FirstOrDefault(status => status.Link.ChannelId == channelId) is not { Link: var link })
		{
			return Notice("That channel is not linked.", NoticeKind.Warn);
		}

		return await gate.SetUnrestrictedAsync(channelId, unrestricted, cancellationToken) is { } problem
			? Notice($"Could not change access in #{link.ChannelName}: {problem}", NoticeKind.Error)
			: Notice(unrestricted
				? $"#{link.ChannelName} is unrestricted. Discord members may speak under their Discord identity without a game account."
				: $"Only linked members may post in #{link.ChannelName} now.", NoticeKind.Ok);
	}

	/// <summary>
	/// <c>@discord/speak</c>: the executor, a character, becomes (or with <c>/off</c> stops being) who its account's
	/// Discord user speaks as in a linked channel. Anyone may run it for themselves.
	/// </summary>
	private static async ValueTask<Option<CallState>> SpeakAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string argument, bool off)
	{
		var services = parser.ServiceProvider;
		var notify = services.GetRequiredService<INotifyService>();
		if (!ulong.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var channelId))
		{
			await notify.Notify(executor, Notice(
				"@discord/speak takes a linked channel's id. In Discord, turn on Developer Mode, then right-click the channel and Copy Channel ID.",
				NoticeKind.Warn));
			return new CallState(BadChannel);
		}

		var identities = services.GetRequiredService<DiscordIdentities>();
		var character = new DiscordCharacter(executor.Object().DBRef, executor.Object().Name, executor.IsPlayer);
		var cancellationToken = ExecutionBudget.CurrentToken;
		var result = off
			? await identities.StopAsync(character, channelId, cancellationToken)
			: await identities.SpeakAsync(character, channelId, () => MaySendAsync(parser, executor, channelId), cancellationToken);
		switch (result)
		{
			case string done:
				await notify.Notify(executor, Notice(done, NoticeKind.Ok));
				return new CallState(channelId.ToString(CultureInfo.InvariantCulture));
			case Error<string> error:
				await notify.Notify(executor, Notice(error.Value, NoticeKind.Error));
				return new CallState("#-1");
			default:
				return CallState.Empty;
		}
	}

	/// <summary>The check <c>@feed/send</c> makes: the kind's runner, or the send lock.</summary>
	private static async Task<bool> MaySendAsync(IMUSHCodeParser parser, AnySharpObject character, ulong channelId)
	{
		var services = parser.ServiceProvider;
		var feeds = services.GetRequiredService<IFeedService>();
		if (await feeds.GetKindAsync(DiscordChannelLink.FeedKind) is not SharpFeedKind kind)
		{
			return false;
		}

		var key = channelId.ToString(CultureInfo.InvariantCulture);
		var feed = await services.GetRequiredService<IMediator>().Send(new GetFeedQuery(kind.Name, key), ExecutionBudget.CurrentToken) switch
		{
			SharpFeed found => found,
			_ => SharpFeed.New(kind.Name, key),
		};
		return await feeds.CanRunAsync(character, kind) || await feeds.PassesAsync(kind, feed, FeedLocks.Send, character);
	}

	private static async ValueTask<MString> UnlinkAsync(DiscordRelay relay, ulong channelId, CancellationToken cancellationToken)
		=> await relay.UnlinkAsync(channelId, cancellationToken) switch
		{
			DiscordChannelLink link => Notice($"Unlinked #{link.ChannelName} and removed its Linked-role requirement. Its feed discord/{link.FeedKey} stays, with its lines.", NoticeKind.Ok),
			Error<string> error => Notice(error.Value, NoticeKind.Error),
			_ => Notice("That channel is not linked.", NoticeKind.Warn),
		};

	private static async ValueTask<MString> ResumeAsync(DiscordRelay relay, ulong channelId, CancellationToken cancellationToken)
		=> await relay.ResumeAsync(channelId, cancellationToken) switch
		{
			DiscordChannelLink link => Notice($"#{link.ChannelName} posts again.", NoticeKind.Ok),
			_ => Notice("That channel is not linked.", NoticeKind.Warn),
		};

	private static async ValueTask<MString> LinkedAsync(IFeedService feeds, DiscordChannelLink link, string? gateProblem)
	{
		var linked = $"Linked #{link.ChannelName} in {link.GuildName}. Lines sent on the feed discord/{link.FeedKey} are posted there.";
		if (await feeds.GetKindAsync(DiscordChannelLink.FeedKind) is Error<string>)
		{
			return Notice($"{linked} Install the discord package for the feed kind they are sent on.", NoticeKind.Warn);
		}

		return gateProblem is null
			? Notice($"{linked} Only linked members may post there.", NoticeKind.Ok)
			: Notice($"{linked} It is still open to everyone: {gateProblem} Fix that, then @discord/gate {link.FeedKey}.", NoticeKind.Warn);
	}

	/// <summary>
	/// <c>@discord/bans</c> says whether game bans are mirrored and lists who the bot banned; <c>/bans on</c> and
	/// <c>/bans off</c> turn mirroring on and off. Turning it off leaves the bans already made until their game bans end.
	/// </summary>
	private static async ValueTask<Option<CallState>> BansAsync(IServiceProvider services, INotifyService notify,
		AnySharpObject executor, string argument)
	{
		switch (argument.ToLowerInvariant())
		{
			case "on" or "off":
				var on = argument.Equals("on", StringComparison.OrdinalIgnoreCase);
				await services.GetRequiredService<DiscordStandingSync>().SetMirrorBansAsync(on, CancellationToken.None);
				await notify.Notify(executor, Notice(on
					? "A game ban now bans the account's Discord member in every server the bot is in. The bot needs Ban Members there."
					: "Game bans are no longer mirrored. Bans the bot already made are lifted when their game bans end.", NoticeKind.Ok));
				return new CallState(on ? "1" : "0");
			case "":
				await notify.Notify(executor, MirroredBans(services.GetRequiredService<IHouseLookService>().Look, services.GetRequiredService<IDiscordSettingsStore>().Get().MirrorBans,
					services.GetRequiredService<IDiscordBanStore>().List()));
				return CallState.Empty;
			default:
				await notify.Notify(executor, Notice("@discord/bans takes on or off.", NoticeKind.Warn));
				return new CallState("#-1 ON OR OFF");
		}
	}

	private static MString MirroredBans(LayoutTheme look, bool mirrorBans, IReadOnlyList<DiscordMirroredBan> bans)
	{
		var setting = ServerLayout.Body(MarkupText.Plain(mirrorBans
			? "A game ban bans the account's Discord member in every server the bot is in. @discord/bans off stops that."
			: "Game bans are not mirrored to Discord. @discord/bans on mirrors them."));
		Block banned = bans.Count == 0
			? ServerLayout.Body(MarkupText.Plain("The bot has banned nobody."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Discord user")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Servers")) { Priority = 2 },
					new TableColumn(MarkupText.Plain("Since")) { Wrap = false },
				],
				bans.Select(ban => ImmutableArray.Create(ban.UserId.ToString(CultureInfo.InvariantCulture),
					ban.Guilds.Count.ToString(CultureInfo.InvariantCulture), ban.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).AsEnumerable()));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Discord bans"), setting, banned), 78, look);
	}

	private static MString Overview(LayoutTheme look, DiscordGatewayState gateway, bool mirrorBans, IReadOnlyList<DiscordLinkStatus> links,
		IDiscordChannelGate gate)
	{
		var connection = ServerLayout.KeyValues(
		[
			("Bot", MarkupText.Plain(gateway.BotName ?? "-")),
			("Connection", MarkupText.Plain(Connection(gateway))),
			("Servers", MarkupText.Plain(gateway.Guilds.ToString(CultureInfo.InvariantCulture))),
			("Game bans", MarkupText.Plain(mirrorBans ? BansMirrored(gateway) : "not mirrored to Discord")),
		]);

		Block linked = links.Count == 0
			? ServerLayout.Body(MarkupText.Plain("No channels are linked. @discord/link <channel id> links one."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Channel")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Server")) { Priority = 2 },
					new TableColumn(MarkupText.Plain("Id")) { Wrap = false, Priority = 3 },
					new TableColumn(MarkupText.Plain("State")) { Min = 10 },
				],
				links.Select(status => ImmutableArray.Create(
					"#" + status.Link.ChannelName, status.Link.GuildName, status.Link.FeedKey,
					State(status, gate.OpenTo(status.Link.ChannelId))).AsEnumerable()));

		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Discord"), connection, linked), 78, look);
	}

	private static string BansMirrored(DiscordGatewayState gateway) => gateway.CannotBanIn is [_, ..] servers
		? $"mirrored, but the bot lacks Ban Members in {string.Join(", ", servers)}"
		: "mirrored to Discord bans";

	private static string Connection(DiscordGatewayState gateway) => gateway.Status switch
	{
		DiscordGatewayStatus.Connected => "connected",
		DiscordGatewayStatus.Connecting => "connecting",
		DiscordGatewayStatus.NoToken => "no bot token; set it with @secret discord.token=<token>, then @discord/connect",
		DiscordGatewayStatus.BadToken => "Discord refused the bot token; set a new one, then @discord/connect",
		DiscordGatewayStatus.IntentsOff =>
			$"turn on {string.Join(" and ", gateway.MissingIntents ?? [])} in the developer portal, then @discord/connect",
		_ => gateway.Detail is { } detail ? $"cannot reach Discord: {detail}" : "cannot reach Discord",
	};

	private static string State(DiscordLinkStatus status, IReadOnlyList<string>? openTo)
	{
		var posting = status.State switch
		{
			DiscordLinkState.Faulted => $"stopped: {status.Link.Fault?.Reason ?? "a post failed"} @discord/resume {status.Link.FeedKey} once it is fixed.",
			DiscordLinkState.Backlogged => $"behind, {status.Depth} lines waiting",
			_ => "posting",
		};
		if (status.Link.PermissionProblem is { } problem) posting += $"; permissions: {problem}";
		if (status.Link.Unrestricted) return $"{posting}; unrestricted (Discord identities allowed)";
		return openTo is [_, ..]
			? $"{posting}; {string.Join(" and ", openTo)} may post without linking: @discord/gate {status.Link.FeedKey}"
			: posting;
	}

	private static MString Notice(string text, NoticeKind kind) => NoticeMarkup.Message(Badge, text, kind);
}
