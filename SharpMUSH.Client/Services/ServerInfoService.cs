using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the anonymous <c>api/server-info</c> facts the portal needs before a visitor
/// authenticates. An answer is kept for <see cref="MaxAge"/>; a newer one that differs raises <see cref="Changed"/>.
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
		string? BuildId = null, string? ImageHosts = null, string? ImageHostList = null, string? Logo = null);

	private const string DefaultMudName = "SharpMUSH";

	private static readonly ServerInfoResponse Fallback = new(true, DefaultMudName, GameFeatures.Defaults);

	private Task<ServerInfoResponse?>? _info;

	/// <summary>
	/// How long an answer is kept before the next reader asks again. Whether a visitor can play as a guest
	/// follows the game's guest roster, so a tab kept open for long must not go on offering or hiding Play on
	/// the answer it had at boot. Settable for tests.
	/// </summary>
	public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(1);

	private DateTimeOffset _answeredAt;

	// The last answer readers were given, kept across a failed read: a newer answer is compared with it, so a
	// change is announced even when the read in between failed and fell back to the defaults.
	private ServerInfoResponse? _lastAnswer;

	// The build of the first answer this tab had. Refresh() does not forget it: it names the bundle running here.
	private string? _firstBuildId;

	/// <summary>
	/// Whether a visitor can play as a guest now (guest logins on and a guest character to hand out). On
	/// any fetch failure this degrades to <c>true</c> — the config default — since the server refuses guest
	/// connects authoritatively regardless of what the client offers.
	/// </summary>
	public virtual async Task<bool> GuestLoginsEnabledAsync() => (await FetchAsync()).GuestsEnabled;

	/// <summary>
	/// The configured game name (<c>Net.MudName</c>). On any fetch failure this degrades to the
	/// config default, <c>"SharpMUSH"</c>.
	/// </summary>
	public virtual async Task<string> GameNameAsync() => (await FetchAsync()).MudName;

	/// <summary>
	/// The game's <c>portal_logo</c>, or <c>null</c> when it has none and the portal draws the SharpMUSH logo. On
	/// any fetch failure this degrades to <c>null</c>.
	/// </summary>
	public virtual async Task<string?> LogoAsync() =>
		(await FetchAsync()).Logo is { Length: > 0 } logo && !string.IsNullOrWhiteSpace(logo) ? logo.Trim() : null;

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
		// Announced here: the readers this wakes are given the newer answer, not told about it twice.
		_lastAnswer = null;
		Changed?.Invoke();
	}

	private async Task<ServerInfoResponse> FetchAsync()
	{
		if (_info is { IsCompletedSuccessfully: true, Result: not null } && DateTimeOffset.UtcNow - _answeredAt > MaxAge)
			_info = null;

		var pending = _info ??= FetchCoreAsync();
		if (await pending is { } info)
		{
			// A newer answer that says something else: readers that asked once (the shell's Play links,
			// FeatureGate) ask again. Every reader of one fetch gets the same instance, so it is announced once.
			var previous = _lastAnswer;
			if (ReferenceEquals(previous, info)) return info;
			_lastAnswer = info;
			if (previous is not null && Differs(previous, info)) Changed?.Invoke();
			return info;
		}

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

	private static bool Differs(ServerInfoResponse before, ServerInfoResponse now) =>
		before.GuestsEnabled != now.GuestsEnabled
		|| before.MudName != now.MudName
		|| before.Logo != now.Logo
		|| !(before.Features ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(now.Features ?? []);

	private ServerInfoResponse Answered(ServerInfoResponse info)
	{
		_answeredAt = DateTimeOffset.UtcNow;
		_firstBuildId ??= info.BuildId;
		PortalImagePolicy.Set(info.ImageHosts, info.ImageHostList);
		return info with { MudName = string.IsNullOrWhiteSpace(info.MudName) ? DefaultMudName : info.MudName };
	}
}
