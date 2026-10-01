using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The server's side of the comm feed (<c>api/comm</c>): a channel's recall buffer, and the acting
/// character's read markers. <see cref="OobCommFeed"/> reads both on load and writes markers as the viewer
/// reads, so unread counts survive a reload and a change of device.
/// </summary>
public interface ICommHistory
{
	/// <summary>The channel's recall buffer, oldest first, each line with the id its <c>comm.message</c> carries.</summary>
	Task<ApiResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(string channel);

	/// <summary>The acting character's read markers.</summary>
	Task<ApiResult<CommReadMarkers>> MarkersAsync();

	/// <summary>Moves a channel's marker on; the server keeps whichever is further.</summary>
	Task<ApiResult<ChannelReadMarker>> MarkChannelAsync(string channel, ReadMarkerUpdate update);

	/// <summary>Moves a conversation's marker on; the server keeps whichever is further.</summary>
	Task<ApiResult<ConversationReadMarker>> MarkConversationAsync(ConversationReadMarkerUpdate update);
}

/// <summary><see cref="ICommHistory"/> over the <c>"api"</c> client, which carries the account session.</summary>
public sealed class CommHistoryService(IHttpClientFactory httpClientFactory) : ICommHistory
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<IReadOnlyList<ChannelRecallLine>>> RecallAsync(string channel) =>
		Client.GetApiAsync<IReadOnlyList<ChannelRecallLine>>(
			$"api/comm/channels/{Uri.EscapeDataString(channel)}/recall", "The server returned no channel history.");

	public Task<ApiResult<CommReadMarkers>> MarkersAsync() =>
		Client.GetApiAsync<CommReadMarkers>("api/comm/markers", "The server returned no read markers.");

	public Task<ApiResult<ChannelReadMarker>> MarkChannelAsync(string channel, ReadMarkerUpdate update) =>
		Client.PutApiAsync<ReadMarkerUpdate, ChannelReadMarker>(
			$"api/comm/markers/channels/{Uri.EscapeDataString(channel)}", update, "The server returned no read marker.");

	public Task<ApiResult<ConversationReadMarker>> MarkConversationAsync(ConversationReadMarkerUpdate update) =>
		Client.PutApiAsync<ConversationReadMarkerUpdate, ConversationReadMarker>(
			"api/comm/markers/conversations", update, "The server returned no read marker.");
}
