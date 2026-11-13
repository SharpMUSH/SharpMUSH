using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Typed client for the server page: its figures (<c>api/admin/server/status</c>) and the updates
/// panel (<c>api/admin/updates</c>).
/// </summary>
public class AdminServerService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<AdminServerStatus>> StatusAsync() =>
		Client.GetApiAsync<AdminServerStatus>("api/admin/server/status", "The server returned no status.");

	public Task<ApiResult<AdminUpdatesStatus>> UpdatesAsync() =>
		Client.GetApiAsync<AdminUpdatesStatus>("api/admin/updates", "The server returned no update status.");

	public Task<ApiResult<UpdateJobStatus>> QueueUpdateAsync(AdminUpdateRequest request) =>
		Client.PostApiAsync<AdminUpdateRequest, UpdateJobStatus>("api/admin/updates", request, "The server did not queue the update.");
}
