using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the server page's figures (<c>api/admin/server/status</c>).</summary>
public class AdminServerService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<AdminServerStatus>> StatusAsync() =>
		Client.GetApiAsync<AdminServerStatus>("api/admin/server/status", "The server returned no status.");
}
