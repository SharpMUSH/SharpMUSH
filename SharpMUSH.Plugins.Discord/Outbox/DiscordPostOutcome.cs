using System.Net;
using Discord.Net;

namespace SharpMUSH.Plugins.Discord.Outbox;

/// <summary>Discord took the post.</summary>
public readonly record struct Posted(ulong MessageId);

/// <summary>Worth trying again after a wait: Discord's servers, the network, or a timeout.</summary>
/// <remarks>
/// A webhook post is not idempotent, so a timeout that Discord did in fact serve comes back as a duplicate line on
/// retry. For chat that is the lesser harm next to a lost one.
/// </remarks>
public readonly record struct Transient(string Reason);

/// <summary>The link cannot post until an admin fixes it. Retrying would only count toward Discord's IP ban.</summary>
/// <param name="Code">Discord's JSON error code, or the HTTP status when Discord gave none.</param>
public readonly record struct LinkBroken(int Code, string Reason);

/// <summary>Discord refused this one message; the link goes on with the next.</summary>
public readonly record struct Rejected(string Reason);

public union PostOutcome(Posted, Transient, LinkBroken, Rejected);

/// <summary>A post that could not be tried at all until an admin acts, such as a link whose webhook token is missing.</summary>
/// <param name="Code">As <see cref="LinkBroken.Code"/>.</param>
public sealed class DiscordLinkException(int code, string reason) : Exception(reason)
{
	public int Code { get; } = code;
}

/// <summary>Sorts what a failed post threw into what the outbox does next.</summary>
public static class DiscordFailures
{
	private const int UnknownChannel = 10003;
	private const int UnknownGuild = 10004;
	public const int UnknownWebhook = 10015;
	private const int MissingAccess = 50001;
	private const int MissingPermissions = 50013;
	public const int InvalidWebhookToken = 50027;

	public static PostOutcome Classify(Exception exception) => exception switch
	{
		HttpException http => Classify(http),
		DiscordLinkException broken => new LinkBroken(broken.Code, broken.Message),
		HttpRequestException or TimeoutException or TaskCanceledException or IOException => new Transient(exception.Message),
		_ => new Rejected(exception.Message),
	};

	/// <summary>Whether <paramref name="broken"/> means the channel's webhook is gone, so a new one could fix it.</summary>
	public static bool WebhookGone(LinkBroken broken) => broken.Code is UnknownWebhook or InvalidWebhookToken or 404;

	private static PostOutcome Classify(HttpException http)
	{
		var code = (int?)http.DiscordCode;
		return (code, http.HttpCode) switch
		{
			(UnknownWebhook or InvalidWebhookToken, _) or (_, HttpStatusCode.NotFound)
				=> new LinkBroken(code ?? 404, "The channel's webhook was deleted or reset."),
			(MissingPermissions, _) => new LinkBroken(MissingPermissions, "The bot lacks a permission it needs in the channel."),
			(MissingAccess, _) => new LinkBroken(MissingAccess, "The bot cannot see the channel."),
			(UnknownChannel, _) => new LinkBroken(UnknownChannel, "The Discord channel was deleted."),
			(UnknownGuild, _) => new LinkBroken(UnknownGuild, "The bot is no longer in that server."),
			(_, HttpStatusCode.Unauthorized) => new LinkBroken(401, "Discord refused the credentials."),
			(_, HttpStatusCode.Forbidden) => new LinkBroken(code ?? 403, "Discord refused the bot access to the channel."),
			(_, >= HttpStatusCode.InternalServerError) or (_, HttpStatusCode.TooManyRequests) => new Transient(http.Reason ?? http.Message),
			_ => new Rejected(http.Reason ?? http.Message),
		};
	}
}
