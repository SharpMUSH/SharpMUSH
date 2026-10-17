using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>
/// The bot's slash commands. <c>/link</c> answers, only to the member who typed it, with a portal link that links
/// their Discord account to the game account they confirm it from; <c>/unlink</c> undoes it.
/// </summary>
/// <remarks>The host's services are resolved when first used: some are built after the plugin's.</remarks>
public sealed class DiscordSlashCommands(
	DiscordAccounts accounts,
	IOptionsWrapper<SharpMUSHOptions> options,
	IServiceProvider services,
	ILogger<DiscordSlashCommands> logger) : IDiscordClientHandler
{
	public const string Link = "link";
	public const string Unlink = "unlink";
	public const string SpeakAs = "speakas";

	/// <summary>The bundled package that turns Discord on; the portal shows the confirm page only while it is installed.</summary>
	public const string PackageId = "discord";

	/// <summary>The portal page a ticket is confirmed on.</summary>
	public const string ConfirmPath = "account/discord/link";

	public void Attach(DiscordSocketClient client)
	{
		client.Ready += () => RegisterAsync(client);
		client.SlashCommandExecuted += command => Background(() => RunAsync(command));
	}

	/// <summary>The commands every server the bot is in gets. Replacing them all at once also drops any left over.</summary>
	public static ApplicationCommandProperties[] Commands =>
	[
		new SlashCommandBuilder().WithName(Link)
			.WithDescription("Link your Discord account to your game account, so you can speak in linked channels.")
			.Build(),
		new SlashCommandBuilder().WithName(Unlink)
			.WithDescription("Unlink your Discord account from your game account.")
			.Build(),
		new SlashCommandBuilder().WithName(SpeakAs)
			.WithDescription("Choose your own game character to speak as in this channel.")
			.AddOption("character", ApplicationCommandOptionType.String, "Your exact character name or objid", isRequired: true)
			.Build(),
	];

	/// <summary>What <c>/link</c> tells the member, given the game's web address (<c>mud_url</c>).</summary>
	public async Task<string> LinkAnswerAsync(DiscordUserCard user)
	{
		if (await services.GetRequiredService<IPackageRegistryService>().GetInstalledPackageAsync(PackageId)
			is not InstalledPackageRecord)
		{
			return "Linking is turned off on this game.";
		}

		if (!Uri.TryCreate(options.CurrentValue.Net.MudUrl?.Trim(), UriKind.Absolute, out var site)
			|| (site.Scheme != Uri.UriSchemeHttps && site.Scheme != Uri.UriSchemeHttp))
		{
			return "This game has no web address set, so linking cannot finish. Ask the game's staff to set mud_url to the website's address.";
		}

		return accounts.StartLink(user) switch
		{
			DiscordLinkTicket ticket =>
				$"Open this link while signed in to the game's website to confirm. It works once, for {DiscordLinkTickets.Lifetime.TotalMinutes:0} minutes:\n{site.GetLeftPart(UriPartial.Path).TrimEnd('/')}/{ConfirmPath}?ticket={ticket.Id}",
			Error<string> error => error.Value,
		};
	}

	public async Task<string> UnlinkAnswerAsync(ulong userId, CancellationToken cancellationToken)
		=> await accounts.UnlinkUserAsync(userId, cancellationToken) switch
		{
			DiscordAccountLink => "Unlinked. Link again any time with /link.",
			_ => "Your Discord account is not linked to a game account.",
		};

	public async Task<string> SpeakAsAnswerAsync(ulong userId, ulong channelId, string character, CancellationToken ct)
	{
		var identities = services.GetRequiredService<DiscordIdentities>();
		return await identities.SpeakAsAsync(userId, channelId, character, async player =>
		{
			var feeds = services.GetRequiredService<IFeedService>();
			if (await feeds.GetKindAsync(Links.DiscordChannelLink.FeedKind) is not SharpMUSH.Library.Models.SharpFeedKind kind) return false;
			var mediator = services.GetRequiredService<Mediator.IMediator>();
			var key = channelId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			var feed = await mediator.Send(new SharpMUSH.Library.Queries.Database.GetFeedQuery(kind.Name, key), ct) is SharpMUSH.Library.Models.SharpFeed found ? found : SharpMUSH.Library.Models.SharpFeed.New(kind.Name, key);
			return !await player.HasFlag("GAGGED") && (await feeds.CanRunAsync(player, kind) || await feeds.PassesAsync(kind, feed, SharpMUSH.Library.Models.FeedLocks.Send, player));
		}, ct) switch
		{ string answer => answer, Error<string> error => error.Value };
	}

	private async Task RegisterAsync(DiscordSocketClient client)
	{
		try
		{
			await client.BulkOverwriteGlobalApplicationCommandsAsync(Commands);
		}
		catch (HttpException exception)
		{
			logger.LogWarning("[Discord] Could not register the slash commands: {Reason}", exception.Message);
		}
	}

	private async Task RunAsync(SocketSlashCommand command)
	{
		switch (command.CommandName)
		{
			case Link:
				var user = new DiscordUserCard(command.User.Id, command.User.Username, command.User.GetDisplayAvatarUrl());
				await command.RespondAsync(await LinkAnswerAsync(user), ephemeral: true);
				break;
			case SpeakAs:
				await command.DeferAsync(ephemeral: true);
				using (var proofTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
				{
					var name = command.Data.Options.FirstOrDefault(option => option.Name == "character")?.Value as string ?? "";
					var answer = command.ChannelId is { } channelId
						? await SpeakAsAnswerAsync(command.User.Id, channelId, name, proofTimeout.Token)
						: "Choose a character in a linked server channel.";
					await command.FollowupAsync(answer, ephemeral: true, allowedMentions: AllowedMentions.None);
				}
				break;
			case Unlink:
				// Taking the Linked role can outlast the three seconds Discord waits for an answer.
				await command.DeferAsync(ephemeral: true);
				using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
				{
					await command.FollowupAsync(await UnlinkAnswerAsync(command.User.Id, timeout.Token), ephemeral: true);
				}

				break;
		}
	}

	/// <summary>Off the gateway's own thread, which waits on each handler.</summary>
	private Task Background(Func<Task> work)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await work();
			}
			catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException
				or OperationCanceledException)
			{
				logger.LogWarning("[Discord] A slash command was not answered: {Reason}", exception.Message);
			}
			catch (Exception exception)
			{
				// Nothing awaits this task; a bug here must reach the log rather than vanish.
				logger.LogError(exception, "[Discord] A slash command failed.");
			}
		});
		return Task.CompletedTask;
	}
}
