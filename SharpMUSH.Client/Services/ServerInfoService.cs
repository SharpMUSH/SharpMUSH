using System.Net.Http.Json;
using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the anonymous <c>api/server-info</c> facts the portal needs before a visitor
/// authenticates. The whole response is fetched once and memoized for the app's lifetime.
/// </summary>
public class ServerInfoService(IHttpClientFactory httpClientFactory)
{
	public record ServerInfoResponse(bool GuestsEnabled, string MudName);

	private const string DefaultMudName = "SharpMUSH";

	private Task<ServerInfoResponse>? _info;

	/// <summary>
	/// Whether the server accepts guest logins (<c>Net.Guests</c>). On any fetch failure this degrades
	/// to <c>true</c> — the config default — since the server refuses guest connects authoritatively
	/// regardless of what the client offers.
	/// </summary>
	public virtual async Task<bool> GuestLoginsEnabledAsync() => (await FetchAsync()).GuestsEnabled;

	/// <summary>
	/// The configured game name (<c>Net.MudName</c>). On any fetch failure this degrades to the
	/// config default, <c>"SharpMUSH"</c>.
	/// </summary>
	public virtual async Task<string> GameNameAsync() => (await FetchAsync()).MudName;

	private Task<ServerInfoResponse> FetchAsync() => _info ??= FetchCoreAsync();

	private async Task<ServerInfoResponse> FetchCoreAsync()
	{
		try
		{
			var client = httpClientFactory.CreateClient("api");
			var info = await client.GetFromJsonAsync<ServerInfoResponse>("api/server-info");
			return info is null
				? new ServerInfoResponse(true, DefaultMudName)
				: info with { MudName = string.IsNullOrWhiteSpace(info.MudName) ? DefaultMudName : info.MudName };
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
		{
			return new ServerInfoResponse(true, DefaultMudName);
		}
	}
}
