using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Channel recall kept as feed lines of the engine's own <see cref="FeedNames.Channel"/> kind, one feed per
/// channel id, so a channel's history survives a restart and counts in <c>@storage</c> like every other feed.
/// Only the lines live here: members, locks, user flags, mogrifiers and delivery stay with the channel code.
/// The kind has no kind row, so <c>@feed</c> and the feed functions cannot read or send into it.
///
/// <para>A line is stored as it was delivered (<see cref="SharpFeedMessage.Line"/>), with the message part alone
/// as its text, and a See_All-only line (PennMUSH's <c>CBTYPE_SEEALL</c>) marked by its
/// <see cref="SharpFeedMessage.Audience"/>.</para>
/// </summary>
public class FeedChannelBufferService(IFeedStore store, IChannelMessageIdSource ids) : IChannelBufferService
{
	/// <summary>Lines a channel keeps; the oldest goes first.</summary>
	public const int BufferSize = 100;

	/// <summary>The <see cref="SharpFeedMessage.Audience"/> of a line only See_All members heard.</summary>
	public const string SeeAllAudience = "seeall";

	private static readonly FeedSettings Limits = new(BufferSize, 0, 0, null, true, FeedStyles.Say);

	public async ValueTask AddMessageAsync(SharpChannelMessage message)
	{
		if (message.Id == 0)
		{
			message.Id = await ids.NextAsync();
		}

		var name = message.SpeakerName;
		await store.AppendFeedMessageAsync(new SharpFeedMessage(message.Id, FeedNames.Channel, message.ChannelId,
			message.Timestamp, message.Sender, name, message.Sender, name, null, "", message.Style,
			MarkupText.Plain(message.MessageText), Line: message.Message,
			Audience: message.SeeAllOnly ? SeeAllAudience : ""), Limits);
	}

	public async IAsyncEnumerable<SharpChannelMessage> GetMessagesAsync(string channelId, int count)
	{
		foreach (var line in await store.GetFeedMessagesAsync(FeedNames.Channel, channelId, count, 0))
		{
			yield return new SharpChannelMessage
			{
				Id = line.Id,
				ChannelId = channelId,
				Timestamp = line.At,
				Sender = line.Speaker,
				Message = line.Line ?? line.Text,
				SeeAllOnly = line.Audience == SeeAllAudience,
				Style = line.Style,
				SpeakerName = line.SpeakerName,
				MessageText = line.Text.ToPlainText()
			};
		}
	}

	public async ValueTask<int> CountMessagesAsync(string channelId)
		=> await store.GetFeedAsync(FeedNames.Channel, channelId) switch
		{
			SharpFeed feed => feed.Messages,
			_ => 0
		};

	/// <remarks>
	/// A line can already be under the new id (broadcast on the renamed channel between the rename and this
	/// move), so the two are merged; lines keep their ids, so recall still reads in order. The merged feed is
	/// trimmed back to <see cref="BufferSize"/> on its next line.
	/// </remarks>
	public async ValueTask MoveBufferAsync(string fromChannelId, string toChannelId)
		=> await store.RenameFeedAsync(FeedNames.Channel, fromChannelId, toChannelId);

	public async ValueTask ClearBufferAsync(string channelId)
		=> await store.DeleteFeedAsync(FeedNames.Channel, channelId);
}
