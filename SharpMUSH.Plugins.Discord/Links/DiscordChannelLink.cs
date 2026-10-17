namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>
/// A Discord channel linked to the feed <c>discord/&lt;channel id&gt;</c>. Lines sent on the feed are posted to the
/// channel through a webhook the bot created there; the webhook's token is the secret named by
/// <see cref="TokenSecret"/>.
/// </summary>
/// <param name="ChannelId">Discord's id for the channel, which never changes; also the feed's key.</param>
/// <param name="GuildId">The server the channel is in.</param>
/// <param name="WebhookId">The webhook the bot created in the channel.</param>
/// <param name="ChannelName">The channel's name when it was linked, for listings; Discord's id is what counts.</param>
/// <param name="GuildName">The server's name when it was linked.</param>
/// <param name="LinkedAt">When it was linked.</param>
/// <param name="LinkedBy">Who linked it, by objid.</param>
/// <param name="Fault">Why the link stopped, until an admin resumes it; null while it posts.</param>
public sealed record DiscordChannelLink(
	ulong ChannelId,
	ulong GuildId,
	ulong WebhookId,
	string ChannelName,
	string GuildName,
	DateTimeOffset LinkedAt,
	string LinkedBy,
	DiscordLinkFault? Fault = null,
	bool Unrestricted = false,
	SharpMUSH.Plugins.Discord.Members.DiscordGateSnapshot? GateSnapshot = null,
	string? PermissionProblem = null,
	bool PermissionHistoryKnown = false)
{
	/// <summary>The feed kind every linked channel's feed belongs to.</summary>
	public const string FeedKind = "discord";

	/// <summary>The feed's key: the channel's id.</summary>
	public string FeedKey => ChannelId.ToString(System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>The secret holding the webhook's token.</summary>
	public string TokenSecret => TokenSecretFor(ChannelId);

	public static string TokenSecretFor(ulong channelId)
		=> $"discord.webhook.{channelId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Why a link stopped posting.</summary>
/// <param name="Code">Discord's error code, or the HTTP status when it gave none.</param>
public sealed record DiscordLinkFault(int Code, string Reason, DateTimeOffset At);
