using Discord;
using Discord.WebSocket;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>Deletes members' messages and sends them DMs, through the bot's current connection.</summary>
public sealed class DiscordMessages : IDiscordClientHandler, IDiscordMessages
{
	private DiscordSocketClient? _client;

	public void Attach(DiscordSocketClient client) => _client = client;

	/// <summary>Nothing happens while the bot is not connected.</summary>
	public async Task DeleteAsync(ulong channelId, ulong messageId, string reason, CancellationToken cancellationToken)
	{
		if (_client is { ConnectionState: ConnectionState.Connected } client && client.GetChannel(channelId) is ITextChannel channel)
		{
			await channel.DeleteMessageAsync(messageId, new RequestOptions { CancelToken = cancellationToken, AuditLogReason = reason });
		}
	}

	/// <summary>Nothing happens while the bot is not connected.</summary>
	public async Task DirectMessageAsync(ulong userId, string text, CancellationToken cancellationToken)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return;
		}

		var options = new RequestOptions { CancelToken = cancellationToken };
		if (await client.GetUserAsync(userId, options) is { } user)
		{
			var direct = await user.CreateDMChannelAsync(options);
			await direct.SendMessageAsync(text, allowedMentions: AllowedMentions.None, options: options);
		}
	}
}
