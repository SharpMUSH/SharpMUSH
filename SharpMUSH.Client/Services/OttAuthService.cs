using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Exchanges MUSH credentials for a short-lived One-Time Token (OTT) via the SharpMUSH
/// Server API.  The OTT is then sent over the WebSocket as <c>connect token &lt;ott&gt;</c>
/// so the plain-text password never travels through the WebSocket connection.
/// </summary>
public class OttAuthService(IHttpClientFactory httpClientFactory, ILogger<OttAuthService> logger)
{
	private record MushTokenRequest(string PlayerName, string Password);
	private record MushTokenResponse(string Token, int ExpiresIn);

	/// <summary>
	/// Request a one-time token for the given MUSH player credentials.
	/// Returns the token string on success, <c>null</c> on failure (bad credentials, server unreachable, etc.).
	/// </summary>
	public async Task<string?> GetTokenAsync(string playerName, string password)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync(
				"api/auth/mush-token",
				new MushTokenRequest(playerName, password));

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("OTT request failed: {Status}", response.StatusCode);
				return null;
			}

			var result = await response.Content.ReadFromJsonAsync<MushTokenResponse>();
			return result?.Token;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "OTT request threw an exception");
			return null;
		}
	}
}
