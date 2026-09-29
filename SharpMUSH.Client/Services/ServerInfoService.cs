namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the anonymous <c>api/server-info</c> facts the portal needs before a visitor
/// authenticates. The response is fetched once and memoized for the app's lifetime.
/// </summary>
/// <remarks>
/// Only an answer is memoized. A failed read is a fact about that moment, not about the game: caching
/// it would pin the brand to <c>"SharpMUSH"</c> and the guest button to the config default for the
/// whole session because the server was restarting when the page first loaded. A failure degrades
/// for the caller that saw it and the next caller asks again.
/// </remarks>
public class ServerInfoService(IHttpClientFactory httpClientFactory)
{
	public record ServerInfoResponse(bool GuestsEnabled, string MudName);

	private const string DefaultMudName = "SharpMUSH";

	private static readonly ServerInfoResponse Fallback = new(true, DefaultMudName);

	private Task<ServerInfoResponse?>? _info;

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

	private async Task<ServerInfoResponse> FetchAsync()
	{
		var pending = _info ??= FetchCoreAsync();
		if (await pending is { } info) return info;

		// Forget the failure only if nobody has started a newer fetch in the meantime.
		_ = Interlocked.CompareExchange(ref _info, null, pending);
		return Fallback;
	}

	private async Task<ServerInfoResponse?> FetchCoreAsync() =>
		await httpClientFactory.CreateClient("api")
				.GetApiAsync<ServerInfoResponse>("api/server-info", "The server returned no server info.") switch
		{
			ServerInfoResponse info =>
				info with { MudName = string.IsNullOrWhiteSpace(info.MudName) ? DefaultMudName : info.MudName },
			ApiFailure => null
		};
}
