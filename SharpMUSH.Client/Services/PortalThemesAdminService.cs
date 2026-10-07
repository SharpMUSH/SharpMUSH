using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the theme editor's API (<c>api/themes</c>).</summary>
public class PortalThemesAdminService(IHttpClientFactory httpClientFactory)
{
	private const string WhenEmpty = "The server described no themes.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>Every theme, the unpublished ones included for an editor.</summary>
	public Task<ApiResult<PortalThemesResponse>> GetAsync() =>
		Client.GetApiAsync<PortalThemesResponse>("api/themes", WhenEmpty);

	public Task<ApiResult<PortalTheme>> CreateAsync(PortalThemeRequest request) =>
		Client.PostApiAsync<PortalThemeRequest, PortalTheme>("api/themes", request, WhenEmpty);

	public Task<ApiResult<PortalTheme>> SaveAsync(string id, PortalThemeRequest request) =>
		Client.PutApiAsync<PortalThemeRequest, PortalTheme>($"api/themes/{Uri.EscapeDataString(id)}", request, WhenEmpty);

	public Task<ApiResult<PortalThemesResponse>> DeleteAsync(string id) =>
		Client.DeleteApiAsync<PortalThemesResponse>($"api/themes/{Uri.EscapeDataString(id)}", WhenEmpty);

	public Task<ApiResult<PortalThemesResponse>> SetDefaultAsync(string id) =>
		Client.PutApiAsync<DefaultThemeRequest, PortalThemesResponse>("api/themes/default", new DefaultThemeRequest(id), WhenEmpty);
}
