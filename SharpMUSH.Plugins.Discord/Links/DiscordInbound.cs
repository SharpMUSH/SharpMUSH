using System.Collections.Concurrent;
using System.Globalization;
using Discord.Net;
using MarkupString;
using MarkupString.Ansi;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>A file attached to a Discord message.</summary>
public sealed record DiscordAttachment(string FileName, string Url);

/// <summary>A message posted in a Discord channel, as the bridge needs it.</summary>
/// <param name="Automated">Posted by a bot or a webhook, the bot's own among them; never relayed, so nothing loops.</param>
/// <param name="ReplyingTo">The author of the message it replies to, by the name Discord shows, or null.</param>
/// <param name="ReplyingToId">That author's Discord id, so a linked author reads as their speaking identity.</param>
/// <param name="Users">The display name of each user it mentions.</param>
public sealed record DiscordIncoming(
	ulong MessageId,
	ulong ChannelId,
	ulong AuthorId,
	string AuthorName,
	bool Automated,
	string Content,
	IReadOnlyList<DiscordAttachment> Attachments,
	string? ReplyingTo,
	ulong? ReplyingToId,
	IReadOnlyDictionary<ulong, string> Users,
	IReadOnlyDictionary<ulong, string> Channels,
	IReadOnlyDictionary<ulong, string> Roles);

/// <summary>A line to speak into a linked channel's feed, as a member's speaking identity.</summary>
/// <param name="AccountId">The game account the Discord author is linked to.</param>
/// <param name="Character">The speaking identity, by objid.</param>
/// <param name="Origin"><c>discord/&lt;message id&gt;</c>, so the line's way back out replaces the message.</param>
public sealed record DiscordSpeech(DiscordChannelLink Link, string AccountId, string Character, string Style, MString Text,
	string Origin);

public readonly record struct Spoken(long LineId);

/// <param name="Reason">Why, said to the Discord author.</param>
public readonly record struct Refused(string Reason);

public union SpeechOutcome(Spoken, Refused);

/// <summary>The game's side of a Discord message: speaking it into a feed, and taking a line back out.</summary>
public interface IDiscordFeedInput
{
	/// <summary>
	/// Sends the line on the link's feed, from the character, once the character is still on the account, the account
	/// active, the character not gagged, and the feed's send lock passed.
	/// </summary>
	Task<SpeechOutcome> SpeakAsync(DiscordSpeech speech, CancellationToken cancellationToken);

	/// <summary>The character's name now, or null when it is gone.</summary>
	Task<string?> NameAsync(string character, CancellationToken cancellationToken);

	/// <summary>Takes a line out of its feed's recall, recording why in the audit log.</summary>
	/// <returns>Whether there was such a line.</returns>
	Task<bool> RemoveLineAsync(long lineId, DiscordChannelLink link, ulong messageId, CancellationToken cancellationToken);
}

/// <summary>What the bridge asks of Discord about members' messages. Needs the bot connected.</summary>
public interface IDiscordMessages
{
	Task DeleteAsync(ulong channelId, ulong messageId, string reason, CancellationToken cancellationToken);

	Task DirectMessageAsync(ulong userId, string text, CancellationToken cancellationToken);
}

/// <summary>
/// Brings members' messages in linked Discord channels into the game, each as a line on the channel's feed spoken by
/// the author's speaking identity there, and takes a line back out of recall when a moderator deletes its message.
/// </summary>
/// <remarks>
/// A message from an unlinked member is deleted, as the Linked role would have stopped it, and the member is told once
/// how to link. A linked member with no identity in the channel is told once how to choose one, and their message
/// stays. Nothing of the message is evaluated.
/// </remarks>
public sealed class DiscordInbound(
	IDiscordLinkStore links,
	IDiscordAccountStore accounts,
	IDiscordSpeakerStore speakers,
	IDiscordMessagePairs pairs,
	IDiscordFeedInput game,
	IDiscordMessages discord,
	ILogger<DiscordInbound> logger)
{
	/// <summary>What each member has been told already this run, so a busy channel does not fill their DMs.</summary>
	private readonly ConcurrentDictionary<(ulong User, string About), bool> _told = new();

	public async Task ReceivedAsync(DiscordIncoming message, CancellationToken cancellationToken)
	{
		if (message.Automated || links.Get(message.ChannelId) is not { } link)
		{
			return;
		}

		if (accounts.Get(message.AuthorId) is not { Linked: true } account)
		{
			await RemoveUnlinkedAsync(message, link, cancellationToken);
			return;
		}

		if (speakers.Character(message.AuthorId, message.ChannelId) is not { } character)
		{
			await TellOnceAsync(message.AuthorId, $"speak:{link.ChannelId}",
				$"Your message in #{link.ChannelName} did not reach the game: you have no character to speak as there. "
				+ $"In the game, as the character, type: @discord/speak {link.FeedKey}", cancellationToken);
			return;
		}

		var (style, content) = Styled(message.Content);
		var text = await TextAsync(message with { Content = content }, link, cancellationToken);
		if (text.ToPlainText().Trim().Length == 0)
		{
			return;
		}

		var speech = new DiscordSpeech(link, account.AccountId, character, style, text,
			$"{DiscordChannelLink.FeedKind}/{message.MessageId.ToString(CultureInfo.InvariantCulture)}");
		if (await game.SpeakAsync(speech, cancellationToken) is Refused refused)
		{
			await TellAsync(message.AuthorId, $"Your message in #{link.ChannelName} did not reach the game: {refused.Reason}",
				cancellationToken);
		}
	}

	/// <summary>A message the bot posted was deleted: the lines it carried leave the game's recall.</summary>
	public async Task DeletedAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken)
	{
		if (pairs.Get(messageId) is not { } pair || pair.ChannelId != channelId || links.Get(channelId) is not { } link)
		{
			return;
		}

		foreach (var lineId in pair.LineIds)
		{
			await game.RemoveLineAsync(lineId, link, messageId, cancellationToken);
		}

		// Last, so a removal that fails part way leaves the pair for the lines still in recall.
		await pairs.RemoveAsync(messageId, cancellationToken);
	}

	/// <summary>
	/// A leading <c>:</c> poses and <c>;</c> semiposes, as with <c>@feed/send</c>; anything else is said.
	/// </summary>
	public static (string Style, string Content) Styled(string content) => content switch
	{
		[':', _, ..] => (FeedStyles.Pose, content[1..]),
		[';', _, ..] => (FeedStyles.SemiPose, content[1..]),
		_ => (FeedStyles.Say, content),
	};

	private async Task<MString> TextAsync(DiscordIncoming message, DiscordChannelLink link, CancellationToken cancellationToken)
	{
		var users = new Dictionary<ulong, string>();
		foreach (var (id, displayName) in message.Users)
		{
			users[id] = await SpeakerNameAsync(id, link, cancellationToken) ?? displayName;
		}

		List<MString> pieces = [];
		if (message.ReplyingTo is { } replyingTo)
		{
			var name = message.ReplyingToId is { } replyId ? await SpeakerNameAsync(replyId, link, cancellationToken) : null;
			pieces.Add(MarkupText.Plain($"(replying to {name ?? replyingTo}) "));
		}

		if (message.Content.Trim().Length > 0)
		{
			pieces.Add(DiscordInboundText.Parse(message.Content.Trim(), new DiscordMentionNames(users, message.Channels, message.Roles)));
		}

		foreach (var attachment in message.Attachments)
		{
			pieces.Add(MarkupText.Plain(pieces.Count == 0 ? "" : " "));
			pieces.Add(MarkupText.Wrap(AnsiMarkup.Create(linkUrl: attachment.Url, linkKind: LinkKind.Url), attachment.FileName));
		}

		return MarkupText.Concat(pieces.ToArray());
	}

	/// <summary>The user's speaking identity on the link, by its name now; never one of their other characters.</summary>
	private async Task<string?> SpeakerNameAsync(ulong userId, DiscordChannelLink link, CancellationToken cancellationToken)
		=> speakers.Character(userId, link.ChannelId) is { } character ? await game.NameAsync(character, cancellationToken) : null;

	private async Task RemoveUnlinkedAsync(DiscordIncoming message, DiscordChannelLink link, CancellationToken cancellationToken)
	{
		try
		{
			await discord.DeleteAsync(message.ChannelId, message.MessageId, "Not linked to a game account", cancellationToken);
		}
		catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
		{
			logger.LogWarning("[Discord] Could not remove an unlinked member's message in #{Channel}: {Reason}",
				link.ChannelName, exception.Message);
		}

		await TellOnceAsync(message.AuthorId, "link",
			$"Only members linked to a game account may post in #{link.ChannelName}, so your message was removed. "
			+ "Type /link to link yours.", cancellationToken);
	}

	private async Task TellOnceAsync(ulong userId, string about, string text, CancellationToken cancellationToken)
	{
		if (_told.TryAdd((userId, about), true))
		{
			await TellAsync(userId, text, cancellationToken);
		}
	}

	/// <summary>A member who takes no DMs from the server's members is not told; that is theirs to choose.</summary>
	private async Task TellAsync(ulong userId, string text, CancellationToken cancellationToken)
	{
		try
		{
			await discord.DirectMessageAsync(userId, text, cancellationToken);
		}
		catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
		{
			logger.LogDebug("[Discord] Could not send a DM: {Reason}", exception.Message);
		}
	}
}
