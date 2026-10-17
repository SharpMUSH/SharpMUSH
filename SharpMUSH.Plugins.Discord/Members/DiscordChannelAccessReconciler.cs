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
			_ = Task.Run(() => ReconcileGuildAsync(guild.Id));
			return Task.CompletedTask;
		};
	}

	/// <summary>One bounded attempt per persisted channel. A failed channel retries on the next reconnect or admin action.</summary>
	public async Task ReconcileGuildAsync(ulong guildId)
	{
		DiscordChannelLink[] channels;
		try { channels = links.List().Where(link => link.GuildId == guildId).ToArray(); }
		catch (Exception error)
		{
			// GuildAvailable dispatches a detached task: an unexpected inventory failure must reach a log,
			// rather than become an unobserved exception. No channel snapshot is available to continue with.
			logger.LogError(error, "[Discord] Could not enumerate channel access for guild {GuildId}.", guildId);
			return;
		}

		foreach (var link in channels)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			try
			{
				if (await access.ReconcileAsync(link.ChannelId, timeout.Token) is { } problem)
					logger.LogWarning("[Discord] Channel {ChannelId} access reconciliation failed: {Problem}", link.ChannelId, problem);
			}
			catch (OperationCanceledException error)
			{
				logger.LogWarning(error, "[Discord] Channel {ChannelId} access reconciliation timed out; continuing with the remaining channels.", link.ChannelId);
			}
			catch (Exception error)
			{
				// Independent channels must still reconcile after a programming/storage failure in one.
				// Keep this background-event boundary broad, but log unexpected faults as errors with identity.
				logger.LogError(error, "[Discord] Unexpected access reconciliation failure for channel {ChannelId}; continuing with the remaining channels.", link.ChannelId);
			}
		}
	}
}
