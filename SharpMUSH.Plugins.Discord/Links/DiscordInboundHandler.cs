using System.Threading.Channels;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>
/// Hands the gateway's message events to <see cref="DiscordInbound"/>, one at a time and in the order Discord sent
/// them, so lines from one channel reach the game in the order they were posted. The gateway's own thread only
/// queues them.
/// </summary>
public sealed class DiscordInboundHandler(DiscordInbound inbound, ILogger<DiscordInboundHandler> logger)
	: BackgroundService, IDiscordClientHandler
{
	/// <summary>Past this many waiting events, new ones are dropped and logged rather than held without end.</summary>
	public const int MaxWaiting = 1000;

	// Wait rather than DropWrite: a full channel then refuses TryWrite, so each drop is logged.
	private readonly Channel<Func<CancellationToken, Task>> _events = Channel.CreateBounded<Func<CancellationToken, Task>>(
		new BoundedChannelOptions(MaxWaiting) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

	public void Attach(DiscordSocketClient client)
	{
		client.MessageReceived += message => Received(message) is { } incoming
			? Queue(cancellationToken => inbound.ReceivedAsync(incoming, cancellationToken))
			: Task.CompletedTask;
		client.MessageDeleted += (message, channel)
			=> Queue(cancellationToken => inbound.DeletedAsync(channel.Id, message.Id, cancellationToken));
		client.MessagesBulkDeleted += (messages, channel) => Queue(async cancellationToken =>
		{
			foreach (var message in messages)
			{
				await inbound.DeletedAsync(channel.Id, message.Id, cancellationToken);
			}
		});
	}

	/// <summary>A member's own message in a server channel, or null for anything else (a system message, a DM).</summary>
	public static DiscordIncoming? Received(SocketMessage message)
	{
		if (message is not SocketUserMessage { Type: MessageType.Default or MessageType.Reply } posted
			|| posted.Channel is not SocketTextChannel channel)
		{
			return null;
		}

		var replied = posted.ReferencedMessage;
		return new DiscordIncoming(
			posted.Id,
			channel.Id,
			posted.Author.Id,
			DisplayName(posted.Author),
			posted.Author.IsBot || posted.Author.IsWebhook,
			posted.Content,
			[.. posted.Attachments.Select(attachment => new DiscordAttachment(attachment.Filename, attachment.Url))],
			replied is null ? null : DisplayName(replied.Author),
			replied?.Author.Id,
			posted.MentionedUsers.DistinctBy(user => user.Id).ToDictionary(user => user.Id, user => DisplayName(user)),
			posted.MentionedChannels.DistinctBy(mentioned => mentioned.Id).ToDictionary(mentioned => mentioned.Id, mentioned => mentioned.Name),
			posted.MentionedRoles.DistinctBy(role => role.Id).ToDictionary(role => role.Id, role => role.Name));
	}

	/// <summary>The name Discord shows for the user in the server: nickname, then display name, then username.</summary>
	private static string DisplayName(IUser user)
		=> (user as IGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var work in _events.Reader.ReadAllAsync(stoppingToken))
		{
			try
			{
				await work(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception)
			{
				// One message that fails must not stop the ones after it.
				logger.LogError(exception, "[Discord] Handling a Discord message failed.");
			}
		}
	}

	private Task Queue(Func<CancellationToken, Task> work)
	{
		if (!_events.Writer.TryWrite(work))
		{
			logger.LogWarning("[Discord] {Count} Discord messages are waiting for the game; one was dropped.", MaxWaiting);
		}

		return Task.CompletedTask;
	}
}
