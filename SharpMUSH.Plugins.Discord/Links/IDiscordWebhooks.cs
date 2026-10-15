using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>Makes the webhook a linked channel posts through. Needs the bot connected, with Manage Webhooks there.</summary>
public interface IDiscordWebhooks
{
	Task<WebhookCreation> CreateWebhookAsync(ulong channelId, CancellationToken cancellationToken);
}

/// <summary>A webhook the bot made, with what the link records about its channel.</summary>
public readonly record struct CreatedWebhook(ulong WebhookId, string Token, ulong GuildId, string GuildName, string ChannelName);

/// <summary>The webhook, or why the bot could not make one, said for the admin who asked.</summary>
public union WebhookCreation(CreatedWebhook, Error<string>);
