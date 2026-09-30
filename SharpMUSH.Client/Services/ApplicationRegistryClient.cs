using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client for the Dynamic Application registry REST API (<c>/api/applications</c>). Used to build
/// nav entries, resolve <c>/apps/{slug}</c>, and drive the admin registration UI. Reads degrade to
/// empty/null on failure; writes are Wizard+ and surface server errors to the caller.
/// </summary>
public class ApplicationRegistryClient(IHttpClientFactory httpClientFactory, ILogger<ApplicationRegistryClient> logger)
{
	// The rail, the drawer and the section sidebars all read this list, and the sidebars re-read on
	// render; they share one read and a short memo. A write through this client refreshes it, and a
	// failed read is not kept.
	private readonly ShortMemo<(bool Ok, IReadOnlyList<PortalApplication> Apps)> _list =
		new(TimeSpan.FromSeconds(60), r => r.Ok);

	/// <summary>Lists all registered applications (caller filters by role for display).</summary>
	public async Task<IReadOnlyList<PortalApplication>> ListAsync() => (await _list.GetAsync(FetchAsync)).Apps;

	private async Task<(bool Ok, IReadOnlyList<PortalApplication> Apps)> FetchAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var apps = await http.GetFromJsonAsync<List<PortalApplication>>("api/applications");
			return (true, apps ?? []);
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to list applications.");
			return (false, []);
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
			logger.LogWarning(ex, "Failed to fetch application {Slug}.", LogSanitizer.Sanitize(slug));
			return null;
		}
	}

	/// <summary>Creates or updates an application; a refusal carries the server's reason.</summary>
	public Task<ApiResult<Success>> UpsertAsync(PortalApplication application)
	{
		_list.Forget();
		return Client.PostApiAsync("api/applications", application);
	}

	/// <summary>Deletes an application by slug.</summary>
	public Task<ApiResult<Success>> DeleteAsync(string slug)
	{
		_list.Forget();
		return Client.DeleteApiAsync($"api/applications/{Uri.EscapeDataString(slug)}");
	}

	private HttpClient Client => httpClientFactory.CreateClient("api");
}
