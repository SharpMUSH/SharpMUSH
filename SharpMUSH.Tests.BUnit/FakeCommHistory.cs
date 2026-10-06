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

	/// <summary>The marker id each conversation pull reached back to, or null.</summary>
	public List<long?> PageRecallAfter { get; } = [];

	/// <summary>Answers as the server does: the last <paramref name="lines"/> (all for 0), reaching back to the page after <paramref name="after"/>.</summary>
	public Task<ApiResult<PageRecall>> ConversationRecallAsync(IReadOnlyList<string> with, int lines, long? after = null)
	{
		PageRecallLines.Add(lines);
		PageRecallAfter.Add(after);
		var key = string.Join(' ', with.Order(StringComparer.Ordinal));
		PageRecalled.Add(key);
		if (FailRecalls-- > 0)
			return Task.FromResult<ApiResult<PageRecall>>(new ApiFailure(ApiFailureKind.Transport, "connection dropped"));
		if (!PageLogging || !PageLog.TryGetValue(key, out var logged))
			return Task.FromResult<ApiResult<PageRecall>>(new PageRecall(PageLogging, []));

		var from = lines == 0 ? 0 : Math.Max(0, logged.Count - lines);
		var unseen = after is { } seen ? logged.FindIndex(line => line.Id > seen) : -1;
		var start = unseen < 0 ? from : Math.Min(unseen, from);
		return Task.FromResult<ApiResult<PageRecall>>(new PageRecall(true, logged.Skip(start).ToArray()));
	}

	/// <summary>The line limit each channel recall asked for (0 for the whole buffer).</summary>
	public List<int> RecallLines { get; } = [];

	/// <summary>The marker id each channel recall reached back to, or null.</summary>
	public List<long?> RecallAfter { get; } = [];

	/// <summary>Answers as the server does: the last <paramref name="lines"/>, reaching back to the line after <paramref name="after"/>.</summary>
	/// <summary>How many of the next channel and conversation recalls fail, as a dropped connection or a 5xx would.</summary>
	public int FailRecalls { get; set; }

	public Task<ApiResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(string channel, int lines, long? after = null)
	{
		Recalled.Add(channel);
		RecallLines.Add(lines);
		RecallAfter.Add(after);
		if (FailRecalls-- > 0)
			return Task.FromResult<ApiResult<IReadOnlyList<ChannelRecallLine>>>(new ApiFailure(ApiFailureKind.Transport, "connection dropped"));
		if (!Recall.TryGetValue(channel, out var buffer))
			return Task.FromResult<ApiResult<IReadOnlyList<ChannelRecallLine>>>(new ApiFailure(ApiFailureKind.NotFound, "no such channel"));

		var from = lines == 0 ? 0 : Math.Max(0, buffer.Count - lines);
		var unseen = after is { } seen ? buffer.FindIndex(line => line.Id > seen) : -1;
		var start = unseen < 0 ? from : Math.Min(unseen, from);
		return Task.FromResult<ApiResult<IReadOnlyList<ChannelRecallLine>>>(buffer.Skip(start).ToArray());
	}

	/// <summary>Each channel's member list, as the who endpoint answers it; a channel not here answers 404.</summary>
	public Dictionary<string, List<ChannelWhoMember>> Who { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The channels whose member list was read, in order.</summary>
	public List<string> WhoRead { get; } = [];

	/// <summary>When set, a who read waits on it, so a test can push while the read is under way.</summary>
	public TaskCompletionSource? WhoGate { get; set; }

	public async Task<ApiResult<ChannelWhoList>> WhoAsync(string channel)
	{
		WhoRead.Add(channel);
		if (WhoGate is { } gate) await gate.Task;
		return Who.TryGetValue(channel, out var members)
			? new ChannelWhoList(channel, members.ToArray())
			: new ApiFailure(ApiFailureKind.NotFound, "no such channel");
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
