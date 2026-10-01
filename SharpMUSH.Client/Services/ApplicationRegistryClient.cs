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
	// render; they share one read and a short memo. A write through this client refreshes it once the
	// server has answered — a read made while the write was in flight saw the old catalog — and a
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
		catch (Exception ex) when (IsUnavailable(ex))
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
		catch (Exception ex) when (IsUnavailable(ex))
		{
			logger.LogWarning(ex, "Failed to fetch application {Slug}.", LogSanitizer.Sanitize(slug));
			return null;
		}
	}

	/// <summary>
	/// A read that could not be answered. These reads take no token, so a cancellation is
	/// <see cref="HttpClient"/>'s timeout, and it means the same as a failed request.
	/// </summary>
	private static bool IsUnavailable(Exception ex) =>
		ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException;

	/// <summary>Creates or updates an application; a refusal carries the server's reason.</summary>
	public Task<ApiResult<Success>> UpsertAsync(PortalApplication application) =>
		ForgettingListAsync(Client.PostApiAsync("api/applications", application));

	/// <summary>Deletes an application by slug.</summary>
	public Task<ApiResult<Success>> DeleteAsync(string slug) =>
		ForgettingListAsync(Client.DeleteApiAsync($"api/applications/{Uri.EscapeDataString(slug)}"));

	/// <summary>
	/// Awaits a write, then drops the memo. A failed write is forgotten too: it may have been applied
	/// before the answer was lost, and one extra read is cheaper than a stale rail.
	/// </summary>
	private async Task<ApiResult<Success>> ForgettingListAsync(Task<ApiResult<Success>> write)
	{
		try
		{
			return await write;
		}
		finally
		{
			_list.Forget();
		}
	}

	private HttpClient Client => httpClientFactory.CreateClient("api");
}
