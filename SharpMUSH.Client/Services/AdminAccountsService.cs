using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the admin accounts API.</summary>
/// <remarks>
/// It does not build its own <c>Authorization</c> header. The <c>"api"</c> client's
/// <see cref="AccountSessionBearerHandler"/> attaches the account-session bearer and hydrates the
/// session from <c>sessionStorage</c> first; a caller that sets the header itself suppresses that
/// hydration and sends a bare <c>Bearer</c> with no value during a page refresh.
/// </remarks>
public class AdminAccountsService(IHttpClientFactory httpClientFactory)
{
	public record AdminCharacterSummary(int DbrefNumber, string Name);

	public record AdminAccountRow(string Id, string Username, string? Email, string Status,
		bool MustChangePassword, bool IsReserved, IReadOnlyList<AdminCharacterSummary> Characters);

	private record ResetPasswordRequest(string NewPassword);

	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<IReadOnlyList<AdminAccountRow>>> ListAsync(string? search = null) =>
		Client.GetApiAsync<IReadOnlyList<AdminAccountRow>>(
			string.IsNullOrWhiteSpace(search)
				? "api/admin/accounts"
				: $"api/admin/accounts?search={Uri.EscapeDataString(search)}",
			"The server returned no account list.");

	public Task<ApiResult<Success>> ResetPasswordAsync(string key, string newPassword) =>
		Client.PostApiAsync($"api/admin/accounts/{Uri.EscapeDataString(key)}/reset-password",
			new ResetPasswordRequest(newPassword));

	public Task<ApiResult<Success>> SetDisabledAsync(string key, bool disabled) =>
		Client.PostApiAsync(
			$"api/admin/accounts/{Uri.EscapeDataString(key)}/{(disabled ? "disable" : "enable")}");

	public Task<ApiResult<Success>> SetStatusAsync(string key, string status) =>
		Client.PostApiAsync($"api/admin/accounts/{Uri.EscapeDataString(key)}/status", new { status });

	public Task<ApiResult<Success>> UnlinkCharacterAsync(string key, int dbrefNumber) =>
		Client.DeleteApiAsync($"api/admin/accounts/{Uri.EscapeDataString(key)}/characters/{dbrefNumber}");
}
