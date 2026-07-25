using System.Net.Http.Json;
using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the anonymous <c>api/server-info</c> facts the portal needs before a visitor
/// authenticates. The result is fetched once and memoized for the app's lifetime.
/// </summary>
public class ServerInfoService(IHttpClientFactory httpClientFactory)
{
	public record ServerInfoResponse(bool GuestsEnabled);

	private Task<bool>? _guestsEnabled;

	/// <summary>
	/// Whether the server accepts guest logins (<c>Net.Guests</c>). Fetched once and memoized for
	/// the app's lifetime. On any fetch failure this degrades to <c>true</c> — the config default —
	/// since the server refuses guest connects authoritatively regardless of what the client offers.
	/// </summary>
	public virtual Task<bool> GuestLoginsEnabledAsync() => _guestsEnabled ??= FetchGuestsEnabledAsync();

	private async Task<bool> FetchGuestsEnabledAsync()
	{
		try
		{
			var client = httpClientFactory.CreateClient("api");
			var info = await client.GetFromJsonAsync<ServerInfoResponse>("api/server-info");
			return info?.GuestsEnabled ?? true;
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
		{
			return true;
		}
	}
}
