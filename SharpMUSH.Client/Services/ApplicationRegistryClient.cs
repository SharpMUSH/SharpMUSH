using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client for the Dynamic Application registry REST API (<c>/api/applications</c>). Used to build
/// nav entries, resolve <c>/apps/{slug}</c>, and drive the admin registration UI. Reads degrade to
/// empty/null on failure; writes are Wizard+ and surface server errors to the caller.
/// </summary>
public class ApplicationRegistryClient(IHttpClientFactory httpClientFactory, ILogger<ApplicationRegistryClient> logger)
{
	/// <summary>Lists all registered applications (caller filters by role for display).</summary>
	public async Task<IReadOnlyList<PortalApplication>> ListAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var apps = await http.GetFromJsonAsync<List<PortalApplication>>("api/applications");
			return apps ?? [];
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to list applications.");
			return [];
		}
	}

	/// <summary>Fetches one application by slug, or <c>null</c> when absent/unavailable.</summary>
	public async Task<PortalApplication?> GetAsync(string slug)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<PortalApplication>($"api/applications/{Uri.EscapeDataString(slug)}");
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to fetch application {Slug}.", slug);
			return null;
		}
	}

	/// <summary>Creates or updates an application. Returns (ok, error-message-when-not-ok).</summary>
	public async Task<(bool Ok, string? Error)> UpsertAsync(PortalApplication application)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/applications", application);
			if (response.IsSuccessStatusCode)
			{
				return (true, null);
			}

			var body = await response.Content.ReadAsStringAsync();
			return (false, ExtractError(body) ?? $"Save failed (HTTP {(int)response.StatusCode}).");
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to upsert application {Slug}.", application.Slug);
			return (false, "Could not reach the server.");
		}
	}

	/// <summary>Deletes an application by slug. Returns true on success.</summary>
	public async Task<bool> DeleteAsync(string slug)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync($"api/applications/{Uri.EscapeDataString(slug)}");
			return response.IsSuccessStatusCode;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to delete application {Slug}.", slug);
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
