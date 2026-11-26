namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>What the plugin asks of Discord about members' messages. Needs the bot connected.</summary>
public interface IDiscordMessages
{
	/// <summary>Deletes a message, with the reason Discord's audit log shows.</summary>
	Task DeleteAsync(ulong channelId, ulong messageId, string reason, CancellationToken cancellationToken);

	/// <summary>Sends a Discord user a direct message.</summary>
	Task DirectMessageAsync(ulong userId, string text, CancellationToken cancellationToken);
}
