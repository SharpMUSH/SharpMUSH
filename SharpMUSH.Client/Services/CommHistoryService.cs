using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The server's side of the comm feed (<c>api/comm</c>): a channel's recall buffer, the acting character's
/// page log, and their read markers. <see cref="OobCommFeed"/> reads both on load and writes markers as the viewer
/// reads, so unread counts survive a reload and a change of device.
/// </summary>
public interface ICommHistory
{
	/// <summary>
	/// The channel's recall buffer, oldest first, each line with the id its <c>comm.message</c> carries: the latest
	/// <paramref name="lines"/> lines (the whole buffer for 0), reaching further back, when <paramref name="after"/>
	/// is given, to the first line after that id.
	/// </summary>
	Task<ApiResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(string channel, int lines, long? after = null);

	/// <summary>Who is on the channel now, as <c>@channel/who</c> lists them for the acting character.</summary>
	Task<ApiResult<ChannelWhoList>> WhoAsync(string channel);

	/// <summary>The acting character's read markers.</summary>
	Task<ApiResult<CommReadMarkers>> MarkersAsync();

	/// <summary>Moves a channel's marker on; the server keeps whichever is further.</summary>
	Task<ApiResult<ChannelReadMarker>> MarkChannelAsync(string channel, ReadMarkerUpdate update);

	/// <summary>Moves a conversation's marker on; the server keeps whichever is further.</summary>
	Task<ApiResult<ConversationReadMarker>> MarkConversationAsync(ConversationReadMarkerUpdate update);

	/// <summary>
	/// The acting character's logged page conversations (the game's <c>page_log</c>), or none with
	/// <see cref="PageConversations.Logging"/> false when the game keeps no page log.
	/// </summary>
	Task<ApiResult<PageConversations>> ConversationsAsync();

	/// <summary>
	/// A page conversation's logged history, oldest first, each page with the id its <c>comm.message</c>
	/// carries. <paramref name="with"/> is the other people in it by objid (the character alone, for pages
	/// to themselves). The latest <paramref name="lines"/> pages (as many as the server gives for 0), reaching
	/// further back, when <paramref name="after"/> is given, to the first page after that id.
	/// </summary>
	Task<ApiResult<PageRecall>> ConversationRecallAsync(IReadOnlyList<string> with, int lines, long? after = null);
}

/// <summary><see cref="ICommHistory"/> over the <c>"api"</c> client, which carries the account session.</summary>
/// <param name="character">
/// The character it reads for, an objid, asked each call; null, or anything not an objid, reads for the session's
/// character. A tab that keeps several characters connected gives each character session its own (<see cref="For"/>).
/// </param>
public sealed class CommHistoryService(IHttpClientFactory httpClientFactory, Func<string?>? character = null) : ICommHistory
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>The same server, read for <paramref name="character"/>.</summary>
	public CommHistoryService For(Func<string?> character) => new(httpClientFactory, character);

	/// <summary><paramref name="url"/> with the character it reads for, when there is one.</summary>
	private string Own(string url) => character?.Invoke() is ['#', ..] objid && objid.Contains(':')
		? $"{url}{(url.Contains('?') ? '&' : '?')}character={Uri.EscapeDataString(objid)}"
		: url;

	public Task<ApiResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(string channel, int lines, long? after = null) =>
		Client.GetApiAsync<IReadOnlyList<ChannelRecallLine>>(
			Own($"api/comm/channels/{Uri.EscapeDataString(channel)}/recall?lines={lines}{(after is { } id ? $"&after={id}" : string.Empty)}"),
			"The server returned no channel history.");

	public Task<ApiResult<ChannelWhoList>> WhoAsync(string channel) =>
		Client.GetApiAsync<ChannelWhoList>(Own($"api/comm/channels/{Uri.EscapeDataString(channel)}/who"),
			"The server returned no member list.");

	public Task<ApiResult<CommReadMarkers>> MarkersAsync() =>
		Client.GetApiAsync<CommReadMarkers>(Own("api/comm/markers"), "The server returned no read markers.");

	public Task<ApiResult<ChannelReadMarker>> MarkChannelAsync(string channel, ReadMarkerUpdate update) =>
		Client.PutApiAsync<ReadMarkerUpdate, ChannelReadMarker>(
			Own($"api/comm/markers/channels/{Uri.EscapeDataString(channel)}"), update, "The server returned no read marker.");

	public Task<ApiResult<ConversationReadMarker>> MarkConversationAsync(ConversationReadMarkerUpdate update) =>
		Client.PutApiAsync<ConversationReadMarkerUpdate, ConversationReadMarker>(
			Own("api/comm/markers/conversations"), update, "The server returned no read marker.");

	public Task<ApiResult<PageConversations>> ConversationsAsync() =>
		Client.GetApiAsync<PageConversations>(Own("api/comm/conversations"), "The server returned no page conversations.");

	public Task<ApiResult<PageRecall>> ConversationRecallAsync(IReadOnlyList<string> with, int lines, long? after = null) =>
		Client.GetApiAsync<PageRecall>(
			Own($"api/comm/conversations/{Uri.EscapeDataString(string.Join(' ', with))}/recall?lines={lines}{(after is { } id ? $"&after={id}" : string.Empty)}"),
			"The server returned no page history.");
}
