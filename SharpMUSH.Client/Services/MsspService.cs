using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the MSSP settings page (<c>api/mssp</c>).</summary>
public class MsspService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<MsspSettingsResponse>> GetAsync() =>
		Client.GetApiAsync<MsspSettingsResponse>("api/mssp", "The server returned no MSSP report.");

	public Task<ApiResult<MsspSettingsResponse>> SaveAsync(Dictionary<string, string[]> settings) =>
		Client.PutApiAsync<MsspSettingsRequest, MsspSettingsResponse>("api/mssp", new MsspSettingsRequest(settings),
			"The server returned no MSSP report.");
}
