using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// An <see cref="ICommHistory"/> answering from what a test set, at once, and recording the markers the
/// feed writes.
/// </summary>
public sealed class FakeCommHistory : ICommHistory
{
	public Dictionary<string, List<ChannelRecallLine>> Recall { get; } = new(StringComparer.OrdinalIgnoreCase);
	public CommReadMarkers? Markers { get; set; }
	public List<string> Recalled { get; } = [];
	public List<(string Channel, ReadMarkerUpdate Update)> ChannelMarks { get; } = [];
	public List<ConversationReadMarkerUpdate> ConversationMarks { get; } = [];

	/// <summary>How many of the next marker writes fail, as a dropped connection or a 5xx would.</summary>
	public int FailMarks { get; set; }

	private bool Fails() => FailMarks-- > 0;

	/// <summary>What the page log lists; null answers as an endpoint that refuses (no character).</summary>
	public PageConversations? PageConversations { get; set; }

	/// <summary>Each conversation's logged pages, by the others' objids, sorted and joined with spaces.</summary>
	public Dictionary<string, List<PageRecallLine>> PageLog { get; } = new(StringComparer.Ordinal);

	/// <summary>Whether the page log answers as a game that keeps one.</summary>
	public bool PageLogging { get; set; } = true;

	/// <summary>The conversations whose history was pulled, by the same key as <see cref="PageLog"/>.</summary>
	public List<string> PageRecalled { get; } = [];

	/// <summary>How many times the conversation list was asked for.</summary>
	public int ConversationListings { get; private set; }

	public Task<ApiResult<PageConversations>> ConversationsAsync()
	{
		ConversationListings++;
		return Task.FromResult<ApiResult<PageConversations>>(PageConversations is { } conversations
			? conversations
			: new ApiFailure(ApiFailureKind.Unauthenticated, "nobody"));
	}

	/// <summary>How many lines each conversation pull asked for.</summary>
	public List<int> PageRecallLines { get; } = [];

	public Task<ApiResult<PageRecall>> ConversationRecallAsync(IReadOnlyList<string> with, int lines)
	{
		PageRecallLines.Add(lines);
		var key = string.Join(' ', with.Order(StringComparer.Ordinal));
		PageRecalled.Add(key);
		return Task.FromResult<ApiResult<PageRecall>>(new PageRecall(PageLogging,
			PageLogging && PageLog.TryGetValue(key, out var logged) ? logged.ToArray() : []));
	}

	/// <summary>The line limit each channel recall asked for.</summary>
	public List<int> RecallLines { get; } = [];

	public Task<ApiResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(string channel, int lines)
	{
		Recalled.Add(channel);
		RecallLines.Add(lines);
		return Task.FromResult<ApiResult<IReadOnlyList<ChannelRecallLine>>>(
			Recall.TryGetValue(channel, out var buffer)
				? buffer.Skip(Math.Max(0, buffer.Count - lines)).ToArray()
				: new ApiFailure(ApiFailureKind.NotFound, "no such channel"));
	}

	public Task<ApiResult<CommReadMarkers>> MarkersAsync() =>
		Task.FromResult<ApiResult<CommReadMarkers>>(Markers is { } markers
			? markers
			: new ApiFailure(ApiFailureKind.Unauthenticated, "nobody"));

	public Task<ApiResult<ChannelReadMarker>> MarkChannelAsync(string channel, ReadMarkerUpdate update)
	{
		ChannelMarks.Add((channel, update));
		return Task.FromResult<ApiResult<ChannelReadMarker>>(Fails()
			? new ApiFailure(ApiFailureKind.Transport, "connection dropped")
			: new ChannelReadMarker(channel, update.LastReadId, update.LastReadAt));
	}

	public Task<ApiResult<ConversationReadMarker>> MarkConversationAsync(ConversationReadMarkerUpdate update)
	{
		ConversationMarks.Add(update);
		return Task.FromResult<ApiResult<ConversationReadMarker>>(Fails()
			? new ApiFailure(ApiFailureKind.Transport, "connection dropped")
			: new ConversationReadMarker(update.With, update.LastReadId, update.LastReadAt));
	}
}
