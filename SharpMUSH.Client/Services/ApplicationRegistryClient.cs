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
	private const string ListRoute = "api/applications";

	private readonly SingleFlight<string, IReadOnlyList<PortalApplication>> _listFlight = new();

	/// <summary>Lists all registered applications (caller filters by role for display).</summary>
	/// <remarks>Concurrent callers (the startup catalog and the nav menu) share one request.</remarks>
	public Task<IReadOnlyList<PortalApplication>> ListAsync() => _listFlight.RunAsync(ListRoute, FetchListAsync);

	private async Task<IReadOnlyList<PortalApplication>> FetchListAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var apps = await http.GetFromJsonAsync<List<PortalApplication>>(ListRoute);
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
			logger.LogWarning(ex, "Failed to fetch application {Slug}.", LogSanitizer.Sanitize(slug));
			return null;
		}
	}

	/// <summary>Creates or updates an application; a refusal carries the server's reason.</summary>
	public Task<ApiResult<Success>> UpsertAsync(PortalApplication application) =>
		Client.PostApiAsync("api/applications", application);

	/// <summary>Deletes an application by slug.</summary>
	public Task<ApiResult<Success>> DeleteAsync(string slug) =>
		Client.DeleteApiAsync($"api/applications/{Uri.EscapeDataString(slug)}");

	private HttpClient Client => httpClientFactory.CreateClient("api");
}
