using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the account bans API (<c>api/admin/bans</c>).</summary>
public class AdminBansService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<AdminBansResponse>> ListAsync() =>
		Client.GetApiAsync<AdminBansResponse>("api/admin/bans", "The server returned no bans.");

	/// <param name="expiresAt">When the ban lifts by itself, or null to keep it until it is lifted.</param>
	public Task<ApiResult<Success>> BanAsync(string accountKey, string reason, DateTimeOffset? expiresAt) =>
		Client.PostApiAsync("api/admin/bans", new AdminBanRequest(accountKey, reason, expiresAt));

	public Task<ApiResult<Success>> LiftAsync(string accountKey) =>
		Client.DeleteApiAsync($"api/admin/bans/{Uri.EscapeDataString(accountKey)}");
}
