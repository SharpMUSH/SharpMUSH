using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the banned player-name list.</summary>
public class BannedNamesService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<string[]>> GetBannedNamesAsync() =>
		Client.GetApiAsync<string[]>("api/bannednames", "The server returned no banned-name list.");

	public Task<ApiResult<Success>> AddBannedNameAsync(string name) =>
		Client.PostApiAsync("api/bannednames", name);

	public Task<ApiResult<Success>> DeleteBannedNameAsync(string name) =>
		Client.DeleteApiAsync($"api/bannednames/{Uri.EscapeDataString(name)}");
}
