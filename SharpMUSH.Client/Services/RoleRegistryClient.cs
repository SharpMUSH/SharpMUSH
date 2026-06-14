using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Client.Models.Roles;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client for the portal Roles REST API (<c>/api/roles</c>). Drives the admin role editor and the
/// account-assignment UI. Reads degrade to empty/null on failure; writes surface server errors to
/// the caller via an (ok, error) tuple where applicable.
/// </summary>
public class RoleRegistryClient(IHttpClientFactory httpClientFactory, ILogger<RoleRegistryClient> logger)
{
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

	/// <summary>Creates or updates a role. Returns (ok, error-message-when-not-ok).</summary>
	public async Task<(bool Ok, string? Error)> UpsertAsync(PortalRoleModel role)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/roles", role);
			if (response.IsSuccessStatusCode)
			{
				return (true, null);
			}

			var body = await response.Content.ReadAsStringAsync();
			return (false, ExtractError(body) ?? $"Save failed (HTTP {(int)response.StatusCode}).");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to upsert role {Slug}.", role.Slug);
			return (false, "Could not reach the server.");
		}
	}

	/// <summary>Deletes a role by slug. Returns (ok, error-message-when-not-ok).</summary>
	public async Task<(bool Ok, string? Error)> DeleteAsync(string slug)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync($"api/roles/{Uri.EscapeDataString(slug)}");
			if (response.IsSuccessStatusCode)
			{
				return (true, null);
			}

			var body = await response.Content.ReadAsStringAsync();
			return (false, ExtractError(body) ?? $"Delete failed (HTTP {(int)response.StatusCode}).");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to delete role {Slug}.", slug);
			return (false, "Could not reach the server.");
		}
	}

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

	/// <summary>Assigns a role to an account. Returns true on success.</summary>
	public async Task<bool> AssignAsync(string accountId, string slug)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsync(
				$"api/roles/account/{Uri.EscapeDataString(accountId)}/{Uri.EscapeDataString(slug)}", null);
			return response.IsSuccessStatusCode;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to assign role {Slug} to {AccountId}.", slug, accountId);
			return false;
		}
	}

	/// <summary>Removes a role from an account. Returns true on success.</summary>
	public async Task<bool> RemoveAsync(string accountId, string slug)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync(
				$"api/roles/account/{Uri.EscapeDataString(accountId)}/{Uri.EscapeDataString(slug)}");
			return response.IsSuccessStatusCode;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to remove role {Slug} from {AccountId}.", slug, accountId);
			return false;
		}
	}

	private static string? ExtractError(string body)
	{
		try
		{
			using var doc = JsonDocument.Parse(body);
			return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
