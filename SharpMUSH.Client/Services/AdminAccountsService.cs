using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the admin accounts API (account-session bearer).</summary>
public class AdminAccountsService(IHttpClientFactory httpClientFactory, AccountAuthService accountAuth)
{
	public record AdminCharacterSummary(int DbrefNumber, string Name);
	public record AdminAccountRow(string Id, string Username, string? Email, bool IsDisabled,
		bool MustChangePassword, IReadOnlyList<AdminCharacterSummary> Characters);
	private record ResetPasswordRequest(string NewPassword);

	private HttpClient CreateClient()
	{
		var http = httpClientFactory.CreateClient("api");
		http.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accountAuth.AccountSessionToken);
		return http;
	}

	public async Task<IReadOnlyList<AdminAccountRow>> ListAsync(string? search = null)
	{
		var http = CreateClient();
		var url = string.IsNullOrWhiteSpace(search) ? "api/admin/accounts" : $"api/admin/accounts?search={Uri.EscapeDataString(search)}";
		return await http.GetFromJsonAsync<IReadOnlyList<AdminAccountRow>>(url) ?? [];
	}

	public async Task<(bool Success, string? Error)> ResetPasswordAsync(string key, string newPassword)
	{
		var response = await CreateClient().PostAsJsonAsync($"api/admin/accounts/{key}/reset-password", new ResetPasswordRequest(newPassword));
		return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
	}

	public async Task<(bool Success, string? Error)> SetDisabledAsync(string key, bool disabled)
	{
		var response = await CreateClient().PostAsync($"api/admin/accounts/{key}/{(disabled ? "disable" : "enable")}", null);
		return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
	}

	public async Task<(bool Success, string? Error)> UnlinkCharacterAsync(string key, int dbrefNumber)
	{
		var response = await CreateClient().DeleteAsync($"api/admin/accounts/{key}/characters/{dbrefNumber}");
		return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
	}
}
