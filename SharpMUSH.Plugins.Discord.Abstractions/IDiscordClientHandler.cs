using Discord.WebSocket;

namespace SharpMUSH.Plugins.Discord;

/// <summary>
/// Part of the plugin that listens to Discord. Each time the bot logs in it gets a new client, which every handler
/// attaches its events to before the client starts, so none of them misses the first Ready.
/// </summary>
public interface IDiscordClientHandler
{
	/// <summary>Subscribes to the new client's events, before it logs in.</summary>
	void Attach(DiscordSocketClient client);
}
