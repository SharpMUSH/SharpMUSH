using SharpMUSH.Library.Models.Portal.Setup;

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
	public record ServerInfoResponse(bool GuestsEnabled, string MudName, IReadOnlyList<string>? Features = null,
		string? BuildId = null);

	private const string DefaultMudName = "SharpMUSH";

	private static readonly ServerInfoResponse Fallback = new(true, DefaultMudName, GameFeatures.Defaults);

	private Task<ServerInfoResponse?>? _info;

	// The build of the first answer this tab had. Refresh() does not forget it: it names the bundle running here.
	private string? _firstBuildId;

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

	/// <summary>
	/// Whether the game has the optional application <paramref name="feature"/> (a <see cref="GameFeatures"/>
	/// id) on. On any fetch failure this degrades to what a new game has, <see cref="GameFeatures.Defaults"/>.
	/// </summary>
	public virtual async Task<bool> HasFeatureAsync(string feature)
		=> (await FetchAsync()).Features?.Contains(feature, StringComparer.OrdinalIgnoreCase) ?? false;

	/// <summary>
	/// The portal build of the first answer this tab had, which is the build it is running; <c>null</c> until
	/// the server has answered once.
	/// </summary>
	public virtual async Task<string?> BuildIdAsync()
	{
		await FetchAsync();
		return _firstBuildId;
	}

	/// <summary>
	/// The portal build the server serves now, asked fresh and not remembered; <c>null</c> when the server did
	/// not answer. Differs from <see cref="BuildIdAsync"/> once the game was deployed under an open tab.
	/// </summary>
	public virtual async Task<string?> CurrentBuildIdAsync() => (await FetchCoreAsync())?.BuildId;

	/// <summary>Raised after <see cref="Refresh"/>: what a reader asked before may have changed.</summary>
	public event Action? Changed;

	/// <summary>
	/// Forgets the remembered answer and tells readers to ask again — after the setup wizard switched an
	/// application on or off, so the navigation follows without a reload.
	/// </summary>
	public void Refresh()
	{
		_info = null;
		Changed?.Invoke();
	}

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
			ServerInfoResponse info => Answered(info),
			ApiFailure => null
		};

	private ServerInfoResponse Answered(ServerInfoResponse info)
	{
		_firstBuildId ??= info.BuildId;
		return info with { MudName = string.IsNullOrWhiteSpace(info.MudName) ? DefaultMudName : info.MudName };
	}
}
