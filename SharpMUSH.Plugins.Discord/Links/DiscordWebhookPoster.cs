using Discord;
using Discord.Rest;
using Discord.Webhook;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Outbox;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>Posts to one linked channel, and owns the webhook it posts through.</summary>
public interface IDiscordLinkPoster : IDiscordPoster, IDisposable
{
	/// <summary>The webhook to post through; a new one (after the old was deleted) is picked up by the next post.</summary>
	ulong WebhookId { get; set; }

	/// <summary>Deletes the webhook from its channel, when the channel is unlinked.</summary>
	Task DeleteWebhookAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Posts to one linked channel through its webhook, as the line's speaker. Needs only the webhook's token, read from
/// the secret store when the first post goes out, so a link posts while the bot's own connection is down.
/// </summary>
public sealed class DiscordWebhookPoster(ulong channelId, ulong webhookId, ISecretStore secrets) : IDiscordLinkPoster
{
	private static readonly DiscordRestConfig Config = new()
	{
		// Discord.Net waits out rate limits; every other failure comes back to the outbox, which owns its retries.
		DefaultRetryMode = RetryMode.RetryRatelimit,
		UseSystemClock = false,
	};

	private readonly SemaphoreSlim _gate = new(1, 1);
	private DiscordWebhookClient? _client;
	private ulong _webhookId = webhookId;

	public ulong WebhookId
	{
		get => Volatile.Read(ref _webhookId);
		set
		{
			Volatile.Write(ref _webhookId, value);
			Interlocked.Exchange(ref _client, null)?.Dispose();
		}
	}

	public async Task<ulong> PostAsync(DiscordPost post, CancellationToken cancellationToken)
	{
		var client = await ClientAsync(cancellationToken);
		// Never @everyone, @here or a role, whatever the game's text says.
		return await client.SendMessageAsync(post.Content, username: post.Speaker, avatarUrl: post.AvatarUrl,
			allowedMentions: AllowedMentions.None, options: new RequestOptions { CancelToken = cancellationToken });
	}

	public async Task DeleteWebhookAsync(CancellationToken cancellationToken)
	{
		var client = await ClientAsync(cancellationToken);
		await client.DeleteWebhookAsync(new RequestOptions { CancelToken = cancellationToken });
	}

	public void Dispose()
	{
		Interlocked.Exchange(ref _client, null)?.Dispose();
		_gate.Dispose();
	}

	private async ValueTask<DiscordWebhookClient> ClientAsync(CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _client) is { } client)
		{
			return client;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_client is { } created)
			{
				return created;
			}

			var token = await secrets.GetAsync(DiscordChannelLink.TokenSecretFor(channelId), cancellationToken) switch
			{
				string value => value,
				NotFound => throw new DiscordLinkException(0, "The channel's webhook token is not stored. Link the channel again."),
				Error<string> error => throw new DiscordLinkException(0, error.Value),
			};

			var webhookId = WebhookId;
			// The constructor looks the webhook up, blocking, and throws when Discord does not know it.
			_client = await Task.Run(() => Create(webhookId, token), cancellationToken);
			return _client;
		}
		finally
		{
			_gate.Release();
		}
	}

	private static DiscordWebhookClient Create(ulong webhookId, string token)
	{
		try
		{
			return new DiscordWebhookClient(webhookId, token, Config);
		}
		catch (InvalidOperationException)
		{
			throw new DiscordLinkException(DiscordFailures.UnknownWebhook, "The channel's webhook was deleted or reset.");
		}
	}
}
