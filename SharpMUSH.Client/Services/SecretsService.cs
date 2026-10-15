using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the server's secrets. Values go up and never come back: only their status does.</summary>
public class SecretsService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<List<SecretStatus>>> ListAsync() =>
		Client.GetApiAsync<List<SecretStatus>>("api/secrets", "The server returned no secrets.");

	public Task<ApiResult<SecretStatus>> SetAsync(string name, string value) =>
		Client.PutApiAsync<SecretSetRequest, SecretStatus>($"api/secrets/{Uri.EscapeDataString(name)}", new SecretSetRequest(value),
			"The server did not say what it stored.");

	public Task<ApiResult<SecretStatus>> ClearAsync(string name) =>
		Client.DeleteApiAsync<SecretStatus>($"api/secrets/{Uri.EscapeDataString(name)}", "The server did not say what it cleared.");
}
