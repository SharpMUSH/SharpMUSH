using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the Messages page's API (<c>api/admin/messages</c>).</summary>
public class GameMessagesService(IHttpClientFactory httpClientFactory)
{
	private const string WhenEmpty = "The server returned no messages.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<GameMessagesResponse>> GetAsync() =>
		Client.GetApiAsync<GameMessagesResponse>("api/admin/messages", WhenEmpty);

	/// <summary>Stores <paramref name="text"/> (ANSI escapes for its colours) as <paramref name="message"/>'s text.</summary>
	public Task<ApiResult<GameMessagesResponse>> SaveAsync(GameMessage message, string text) =>
		Client.PutApiAsync<GameMessageTextRequest, GameMessagesResponse>(
			$"api/admin/messages/{message}", new GameMessageTextRequest(text), WhenEmpty);

	/// <summary>Puts back the text SharpMUSH ships for <paramref name="message"/>.</summary>
	public Task<ApiResult<GameMessagesResponse>> ResetAsync(GameMessage message) =>
		Client.DeleteApiAsync<GameMessagesResponse>($"api/admin/messages/{message}", WhenEmpty);

	/// <summary>Chooses where messages come from; null follows the Messages package again.</summary>
	public Task<ApiResult<GameMessagesResponse>> SetSourceAsync(GameMessageSource? source) =>
		Client.PutApiAsync<GameMessageSourceRequest, GameMessagesResponse>(
			"api/admin/messages/source", new GameMessageSourceRequest(source), WhenEmpty);
}
