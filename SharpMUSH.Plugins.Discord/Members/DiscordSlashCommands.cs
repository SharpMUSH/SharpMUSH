using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>
/// The bot's slash commands. <c>/link</c> answers, only to the member who typed it, with a portal link that links
/// their Discord account to the game account they confirm it from; <c>/unlink</c> undoes it.
/// </summary>
public sealed class DiscordSlashCommands(
	DiscordAccounts accounts,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<DiscordSlashCommands> logger) : IDiscordClientHandler
{
	public const string Link = "link";
	public const string Unlink = "unlink";

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
	];

	/// <summary>What <c>/link</c> tells the member, given the game's web address (<c>mud_url</c>).</summary>
	public string LinkAnswer(DiscordUserCard user)
	{
		if (options.CurrentValue.Net.MudUrl is not { Length: > 0 } site)
		{
			return "This game has no web address set, so linking cannot finish. Ask the game's staff to set mud_url.";
		}

		return accounts.StartLink(user) switch
		{
			DiscordLinkTicket ticket =>
				$"Open this link while signed in to the game's website to confirm. It works once, for {DiscordLinkTickets.Lifetime.TotalMinutes:0} minutes:\n{site.TrimEnd('/')}/{ConfirmPath}?ticket={ticket.Id}",
			Error<string> error => error.Value,
		};
	}

	public async Task<string> UnlinkAnswerAsync(ulong userId, CancellationToken cancellationToken)
		=> await accounts.UnlinkUserAsync(userId, cancellationToken) switch
		{
			DiscordAccountLink => "Unlinked. Link again any time with /link.",
			_ => "Your Discord account is not linked to a game account.",
		};

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
				await command.RespondAsync(LinkAnswer(user), ephemeral: true);
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
