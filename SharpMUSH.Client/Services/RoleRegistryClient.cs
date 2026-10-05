using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Client.Models.Roles;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client for the portal Roles REST API (<c>/api/roles</c>). Drives the admin role editor and the
/// account-assignment UI. Reads degrade to empty/null on failure; writes answer with
/// <see cref="ApiResult{T}"/>, so a refusal reaches the page with the server's reason.
/// </summary>
public class RoleRegistryClient(IHttpClientFactory httpClientFactory, ILogger<RoleRegistryClient> logger)
{
	public async Task<IReadOnlyDictionary<string, SharpMUSH.Library.Authorization.PermissionExplanation>> EffectiveAsync()
	{
		try
		{
			return await httpClientFactory.CreateClient("api").GetFromJsonAsync<Dictionary<string, SharpMUSH.Library.Authorization.PermissionExplanation>>("api/roles/effective") ?? [];
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to load effective permissions.");
			return new Dictionary<string, SharpMUSH.Library.Authorization.PermissionExplanation>();
		}
	}

	/// <summary>Lists every defined role (caller sorts/filters for display).</summary>
	public async Task<IReadOnlyList<PortalRoleModel>> ListAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var roles = await http.GetFromJsonAsync<List<PortalRoleModel>>("api/roles");
			return roles ?? [];
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to list roles.");
			return [];
		}
	}

	/// <summary>Creates or updates a role; a refusal carries the server's reason.</summary>
	public Task<ApiResult<Success>> UpsertAsync(PortalRoleModel role) =>
		Client.PostApiAsync("api/roles", role);

	/// <summary>Deletes a role by slug.</summary>
	public Task<ApiResult<Success>> DeleteAsync(string slug) =>
		Client.DeleteApiAsync($"api/roles/{Uri.EscapeDataString(slug)}");

	/// <summary>Looks up an account and its role assignments by username, or <c>null</c> when absent/unavailable.</summary>
	public async Task<AccountRolesModel?> LookupAccountAsync(string username)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync($"api/roles/account?username={Uri.EscapeDataString(username)}");
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				return null;
			}

			response.EnsureSuccessStatusCode();
			return await response.Content.ReadFromJsonAsync<AccountRolesModel>();
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to look up account {Username}.", username);
			return null;
		}
	}

	/// <summary>Assigns a role to an account.</summary>
	public Task<ApiResult<Success>> AssignAsync(string accountId, string slug) =>
		Client.PostApiAsync($"api/roles/account/{Uri.EscapeDataString(accountId)}/{Uri.EscapeDataString(slug)}");

	/// <summary>Removes a role from an account.</summary>
	public Task<ApiResult<Success>> RemoveAsync(string accountId, string slug) =>
		Client.DeleteApiAsync($"api/roles/account/{Uri.EscapeDataString(accountId)}/{Uri.EscapeDataString(slug)}");

	/// <summary>Sets one per-account override (<c>Allow</c>, <c>Deny</c>, or <c>Inherit</c> to clear it).</summary>
	public Task<ApiResult<Success>> SetOverrideAsync(string accountId, string scope, string state) =>
		Client.PutApiAsync($"api/roles/account/{Uri.EscapeDataString(accountId)}/overrides", new OverrideRequest(scope, state));

	private sealed record OverrideRequest(string Scope, string State);

	private HttpClient Client => httpClientFactory.CreateClient("api");
}
