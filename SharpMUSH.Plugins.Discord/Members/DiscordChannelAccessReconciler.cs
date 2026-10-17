using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>Retry persisted desired access when the bot reconnects; failures remain visible on the link.</summary>
public sealed class DiscordChannelAccessReconciler(IDiscordLinkStore links, IDiscordChannelGate access, ILogger<DiscordChannelAccessReconciler> logger) : IDiscordClientHandler
{
	public void Attach(DiscordSocketClient client)
	{
		client.GuildAvailable += guild =>
		{
			_ = Task.Run(async () =>
			{
				try
				{
					foreach (var link in links.List().Where(link => link.GuildId == guild.Id))
					{
						using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
						await access.ReconcileAsync(link.ChannelId, timeout.Token);
					}
				}
				catch (Exception error) { logger.LogWarning(error, "[Discord] Channel access reconciliation did not finish."); }
			});
			return Task.CompletedTask;
		};
	}
}
