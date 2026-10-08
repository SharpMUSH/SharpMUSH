using SharpMUSH.Library.API;
using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.DiscriminatedUnions;
using static SharpMUSH.Client.Services.AccountApiClient;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The tab's account session: signing in and out, the session's authority, and the character the tab
/// acts as. Passwords are never stored.
/// </summary>
/// <remarks>
/// <para>It composes three narrower parts rather than doing their work itself:
/// <see cref="AccountApiClient"/> (the wire — every call answers with an <see cref="ApiResult{T}"/>),
/// <see cref="AccountSessionStorage"/> (the tab-scoped <c>sessionStorage</c> keys) and
/// <see cref="ActiveCharacterState"/> (the roster and the acting character). What is left here is the
/// ordering between them — persist before adopting, end the session's other holders before forgetting
/// it — which is what the rest of the portal depends on. The game-side teardown at sign-out belongs to
/// whoever registers an <see cref="IAccountSessionEndingHandler"/>.</para>
///
/// <para>The constructor is unchanged from before the split, and the parts are built here rather
/// than injected: they have no other consumer, and the tests construct this type directly.</para>
/// </remarks>
public class AccountAuthService(
	IHttpClientFactory httpClientFactory,
	IJSRuntime js,
	ILogger<AccountAuthService> logger,
	IEnumerable<IAccountSessionEndingHandler> sessionEndingHandlers) : IAccountAuthState
{
	private const string SessionNotSavedMessage =
		"This browser tab could not save your session. Allow this site to store data, then sign in again.";

	private const string NotLoggedInMessage = "Not logged in.";

	private readonly AccountApiClient _api = new(httpClientFactory);
	private readonly AccountSessionStorage _storage = new(js);
	private readonly ActiveCharacterState _roster = new(logger);
	private readonly PasskeyInterop _passkeys = new(js);

	/// <summary><paramref name="IsActing"/> is the server's answer to "who is this tab?" — the acting
	/// character is bound to the session token, which is opaque here, so the roster carries it.</summary>
	/// <param name="ThemeId">The character's chosen portal theme, or null for the game's default.</param>
	/// <param name="Accent">The character's own accent colour, or null for the theme's.</param>
	/// <param name="Vision">The player's colour vision (<see cref="SharpMUSH.Library.Models.Portal.ThemeVision"/>), or null for typical.</param>
	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags, bool IsActing = false,
		string? ThemeId = null, string? Accent = null, string? Vision = null);

	public record DebugOttResponse(string Token, int ExpiresIn, string PlayerName,
		string? AccountId, string? AccountUsername, string? AccountSessionToken, bool AccountMustChangePassword);

	/// <summary>A first-run claim that went through.</summary>
	/// <param name="SignedIn">
	/// Whether this tab is now signed in as the new administrator. The claim stands either way; a
	/// server that could not mint the session, or a tab that could not keep it, only loses the
	/// automatic sign-in.
	/// </param>
	public sealed record SetupClaimed(bool SignedIn);

	/// <summary>An unlink that went through.</summary>
	/// <param name="Advisory">
	/// Set when the unlink landed but something after it did not — the session could not be rebound to
	/// another character — and the player has to know to pick one. Not a failure: the character is gone.
	/// </param>
	public sealed record CharacterUnlinked(string? Advisory);

	public string? AccountSessionToken { get; private set; }
	public string? Username { get; private set; }
	public IReadOnlyList<CharacterSummary> Characters => _roster.Characters;
	public bool MustChangePassword { get; private set; }
	public bool IsLoggedIn => AccountSessionToken is not null;
	public string? Role { get; private set; }
	public IReadOnlyList<string> Permissions { get; private set; } = [];

	/// <summary>
	/// The character this tab is currently acting as: the one the server's roster marks, reassigned by
	/// <see cref="SwitchCharacterAsync"/>. Null when the account holds no characters or the session is
	/// bound to none. See <see cref="ActiveCharacterState"/>.
	/// </summary>
	public CharacterSummary? ActiveCharacter => _roster.ActiveCharacter;

	/// <summary>Raised whenever <see cref="ActiveCharacter"/> changes to a different character.</summary>
	public event Action? ActiveCharacterChanged
	{
		add => _roster.Changed += value;
		remove => _roster.Changed -= value;
	}

	/// <inheritdoc cref="ActiveCharacterState.AppearanceChanged"/>
	public event Action? AppearanceChanged
	{
		add => _roster.AppearanceChanged += value;
		remove => _roster.AppearanceChanged -= value;
	}

	/// <inheritdoc cref="ActiveCharacterState.SetActive"/>
	public void SetActiveCharacter(CharacterSummary? character) => _roster.SetActive(character);

	/// <summary>
	/// True once the user has explicitly logged out in this tab (sessionStorage-latched).
	/// Guards against dev-mode debug re-auth (and any other silent re-login) undoing an
	/// explicit logout on the next component init/reload — cleared by any successful
	/// login/register/setup completion.
	/// </summary>
	public bool ExplicitlyLoggedOut { get; private set; }

	/// <summary>Raised whenever login/logout changes the session; AccountAuthStateProvider subscribes.</summary>
	public event Action? AuthStateChanged;

	private Task? _initTask;
	private Task<DebugOttResponse?>? _debugOttTask;

	/// <summary>Keyed by session token, so a sign-in mid-request never joins the previous session's roster read.</summary>
	private readonly SingleFlight<string, ApiResult<IReadOnlyList<CharacterSummary>>> _charactersFlight = new(StringComparer.Ordinal);

	/// <summary>
	/// Single-flight, idempotent hydration: the first caller kicks off <see cref="InitCoreAsync"/>
	/// and every caller (that one and any later one, concurrent or sequential) awaits the very
	/// same task instead of re-reading storage. This matters because hydration is no longer only
	/// triggered by MainLayout — CascadingAuthenticationState (App.razor root) and any auth-state
	/// query can now trigger it too, and on a page refresh those can race MainLayout's own call.
	/// The <c>??=</c> is race-safe here specifically because Blazor WASM is single-threaded: there
	/// is no window between reading <see cref="_initTask"/> and assigning it where another
	/// call could interleave and start a second <see cref="InitCoreAsync"/>.
	/// </summary>
	public Task InitAsync() => _initTask ??= InitCoreAsync();

	private async Task InitCoreAsync()
	{
		// Force a genuine suspension before touching any state. RaiseAuthStateChanged below can
		// synchronously re-enter InitAsync() (DebugAuthStateProvider subscribes to AuthStateChanged
		// and calls back into the account-auth state from its handler). If every await from here on
		// happened to complete synchronously (as a test fake IJSRuntime does), the whole method body —
		// including that re-entrant notification — would run to completion before the
		// `_initTask ??= InitCoreAsync()` assignment in InitAsync() ever lands, so the re-entrant call
		// would see a still-null _initTask and kick off a second, infinitely-recursing InitCoreAsync().
		// Yielding here guarantees InitCoreAsync()'s Task is cached in _initTask before any of the
		// body (or its re-entrant fallout) executes.
		await Task.Yield();

		try
		{
			await HydrateFromStorageAsync();
		}
		catch (Exception ex)
		{
			// InitAsync caches the TASK, not its result, so a hydration that throws is latched for the
			// life of the tab: GlobalTerminal, MainLayout, both auth-state providers and the bearer
			// handler all await this same task, and every one of them would rethrow forever — a portal
			// that cannot render any page rather than one page degrading. State that cannot be restored
			// (a corrupted permissions blob; blocked storage already reads as empty) is indistinguishable
			// from a tab with no session, and "no session" is a state the whole portal already handles.
			logger.LogError(ex, "Account session hydration failed; treating this tab as signed out");
			ClearSessionState();
		}

		// CascadingAuthenticationState snapshots before MainLayout's InitAsync runs; re-notify so a
		// reloaded tab's restored session (or the cleared one above) reaches [Authorize] gates.
		RaiseAuthStateChanged();
	}

	private async Task HydrateFromStorageAsync()
	{
		ExplicitlyLoggedOut = await _storage.IsLoggedOutAsync();

		if (ExplicitlyLoggedOut)
		{
			ClearSessionState();
			return;
		}

		if (await _storage.ReadAsync() is not AccountSessionStorage.StoredSession stored)
		{
			// No session in this tab (sessionStorage is tab-scoped): don't restore Username/Role/
			// Permissions — a returning user in a new tab would otherwise get a phantom identity with
			// no live session. Nothing in the portal pre-fills the login form from Username, so
			// there's no UX reason to keep it around. A browser that was told to remember the account
			// signs this tab in from that instead.
			ClearSessionState();
			await ResumeRememberedLoginAsync(character: null);
			return;
		}

		AccountSessionToken = stored.Token;
		Username = stored.Username;
		MustChangePassword = stored.MustChangePassword;
		// Stored grants are never authority: roles can migrate or be revoked while this tab is closed.
		Role = null;
		Permissions = [];
		switch (await LoadSessionAuthorityAsync(AccountSessionToken))
		{
			case SessionAuthorityLoad.Failed:
				// The credential stays usable, but with no role or grant the tab is a Guest until the server
				// answers. InitAsync is cached for the life of the tab, so without this one timed-out refresh
				// — a server restarting, a dropped request — would leave it that way until a reload.
				_ = RetrySessionAuthorityAsync();
				break;
			case SessionAuthorityLoad.SignedOut:
				// The tab's own session ran out while it was closed or idle; the remembered login, if this
				// browser has one, outlasts it.
				await ResumeRememberedLoginAsync(character: null);
				break;
		}
	}

	/// <summary>
	/// Signs this tab in from the browser's remembered login, bound to <paramref name="character"/> when
	/// the account still owns it; false, with nothing changed, when there is none to use.
	/// </summary>
	private async Task<bool> ResumeRememberedLoginAsync(CharacterSummary? character)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		return await _api.ResumeAsync(character, timeout.Token) switch
		{
			LoginResponse session => await SignedInAsync(session) is IReadOnlyList<CharacterSummary>,
			// The usual answer: this browser was not told to remember anyone, or the login lapsed.
			ApiFailure { Kind: ApiFailureKind.Unauthenticated } => false,
			ApiFailure failure => NotResumed(failure),
		};

		bool NotResumed(ApiFailure failure)
		{
			logger.LogWarning("Could not resume the remembered login: {Message}", failure.Message);
			return false;
		}
	}

	/// <summary>The renewal in flight, shared by every request its token failed on.</summary>
	private Task<string?>? _renewal;

	/// <summary>The last token the remembered login could not renew; requests on it are not retried.</summary>
	private string? _unrenewable;

	public async Task<string?> RenewSessionAsync(string rejectedToken)
	{
		await InitAsync();
		if (AccountSessionToken is not { } current) return null;
		if (current != rejectedToken) return current;
		if (current == _unrenewable) return null;
		return await (_renewal ??= RenewCoreAsync(rejectedToken));
	}

	private async Task<string?> RenewCoreAsync(string rejectedToken)
	{
		// Yield first, so _renewal is set before this can finish and clear it.
		await Task.Yield();
		try
		{
			if (await ResumeRememberedLoginAsync(ActiveCharacter) && AccountSessionToken is { } renewed)
				return renewed;
			_unrenewable = rejectedToken;
			return null;
		}
		finally
		{
			_renewal = null;
		}
	}

	private enum SessionAuthorityLoad { Loaded, SignedOut, Failed, Superseded }

	/// <summary>How long to wait between attempts after the session's authority could not be loaded.
	/// The last delay repeats until <see cref="SessionAuthorityRetryLimit"/>. Settable for tests.</summary>
	public IReadOnlyList<TimeSpan> SessionAuthorityRetryDelays { get; set; } =
		[TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

	/// <summary>How long to keep retrying before leaving the tab as it is.</summary>
	public TimeSpan SessionAuthorityRetryLimit { get; set; } = TimeSpan.FromMinutes(10);

	/// <summary>Loads the current role, grants and name for <paramref name="token"/> from the server.</summary>
	private async Task<SessionAuthorityLoad> LoadSessionAuthorityAsync(string token)
	{
		// Authentication-state queries share the bootstrap task; a stalled refresh must not hold
		// public rendering for the named client's much longer default timeout.
		using var refresh = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var result = await _api.SessionAsync(token, refresh.Token);
		if (AccountSessionToken != token) return SessionAuthorityLoad.Superseded;

		return result switch
		{
			SessionStateResponse current => AdoptAuthority(current),
			ApiFailure { Kind: ApiFailureKind.Unauthenticated } => SignedOut(),
			ApiFailure failure => Failed(failure),
		};

		SessionAuthorityLoad SignedOut()
		{
			ClearSessionState();
			return SessionAuthorityLoad.SignedOut;
		}

		SessionAuthorityLoad Failed(ApiFailure failure)
		{
			// No stored role or grant has been restored, so permission-gated controls stay closed.
			logger.LogWarning("Could not refresh account session permissions: {Message}", failure.Message);
			return SessionAuthorityLoad.Failed;
		}
	}

	private SessionAuthorityLoad AdoptAuthority(SessionStateResponse current)
	{
		Username = current.Username;
		MustChangePassword = current.MustChangePassword;
		Role = current.Role;
		Permissions = current.Permissions ?? [];
		return SessionAuthorityLoad.Loaded;
	}

	/// <summary>Bumped whenever the tab's authority is replaced wholesale — a sign-in, a sign-out, a
	/// session dropped — and not by a character switch, which keeps the same account and its grants.</summary>
	private int _authorityGeneration;

	/// <summary>
	/// Asks again, backing off, until the server answers or the tab's account changes (a sign-in or a
	/// sign-out), then tells the portal so gated controls reappear. Each attempt uses the token the tab
	/// holds at that moment: a character switch adopts a new token for the same account without loading
	/// its authority, so a recovery keyed to the old token would give up and leave the tab a Guest.
	/// </summary>
	private async Task RetrySessionAuthorityAsync()
	{
		var generation = _authorityGeneration;
		var deadline = DateTimeOffset.UtcNow + SessionAuthorityRetryLimit;
		for (var attempt = 0; DateTimeOffset.UtcNow < deadline; attempt++)
		{
			await Task.Delay(SessionAuthorityRetryDelays[Math.Min(attempt, SessionAuthorityRetryDelays.Count - 1)]);
			if (generation != _authorityGeneration || AccountSessionToken is not { } token) return;

			switch (await LoadSessionAuthorityAsync(token))
			{
				case SessionAuthorityLoad.Loaded or SessionAuthorityLoad.SignedOut:
					RaiseAuthStateChanged();
					return;
					// Superseded: the token changed while the request was out. The generation check at the
					// top of the next attempt tells a switch (carry on) from a sign-in or sign-out (stop).
			}
		}
	}

	/// <summary>
	/// Re-reads the role and grants after the account's characters change (the server derives them from
	/// the characters) and tells the portal, so gated controls follow at once.
	/// </summary>
	private async Task ReloadAuthorityAsync()
	{
		if (AccountSessionToken is not { } token) return;
		switch (await LoadSessionAuthorityAsync(token))
		{
			case SessionAuthorityLoad.Loaded or SessionAuthorityLoad.SignedOut:
				RaiseAuthStateChanged();
				break;
			case SessionAuthorityLoad.Failed:
				// One dropped request would otherwise leave the tab on the role it had before (a Guest after
				// its first character, or staff controls after unlinking the character that granted them).
				_ = RetrySessionAuthorityAsync();
				break;
		}
	}

	/// <summary>Everything a tab holding no usable session must look like. Does not raise
	/// <see cref="AuthStateChanged"/> — the caller decides when the notification is due.</summary>
	private void ClearSessionState()
	{
		_authorityGeneration++;
		AccountSessionToken = null;
		Username = null;
		MustChangePassword = false;
		Role = null;
		Permissions = [];
		SetActiveCharacter(null);
	}

	/// <summary>Signs in with a name or email and a password; answers with the account's roster.</summary>
	/// <param name="rememberMe">Also keep this browser signed in, for tabs opened later and for this one once
	/// its own session runs out.</param>
	public async Task<ApiResult<IReadOnlyList<CharacterSummary>>> LoginAsync(string identifier, string password,
		bool rememberMe = false) =>
		await _api.LoginAsync(identifier, password, rememberMe) switch
		{
			LoginResponse session => await SignedInAsync(session),
			ApiFailure failure => Logged(failure, "Account login"),
		};

	/// <inheritdoc cref="PasskeyInterop.IsSupportedAsync"/>
	public Task<bool> PasskeysSupportedAsync() => _passkeys.IsSupportedAsync();

	/// <summary>
	/// Signs in with a passkey the visitor picks in the browser's prompt; the passkey names the account.
	/// Answers with the account's roster, as <see cref="LoginAsync"/> does.
	/// </summary>
	/// <param name="rememberMe">As on <see cref="LoginAsync"/>.</param>
	public async Task<PasskeyOutcome<IReadOnlyList<CharacterSummary>>> LoginWithPasskeyAsync(bool rememberMe = false) =>
		await _api.PasskeyLoginOptionsAsync() switch
		{
			PasskeyChallenge challenge => await SignInWithPasskeyAsync(challenge, rememberMe),
			ApiFailure failure => Logged(failure, "Passkey login"),
		};

	private async Task<PasskeyOutcome<IReadOnlyList<CharacterSummary>>> SignInWithPasskeyAsync(PasskeyChallenge challenge,
		bool rememberMe) =>
		await _passkeys.GetAsync(challenge.Options) switch
		{
			JsonElement credential => await PasskeySignedInAsync(challenge.CeremonyId, credential, rememberMe),
			PasskeyCancelled cancelled => cancelled,
			ApiFailure failure => Logged(failure, "Passkey prompt"),
		};

	private async Task<PasskeyOutcome<IReadOnlyList<CharacterSummary>>> PasskeySignedInAsync(string ceremonyId,
		JsonElement credential, bool rememberMe) =>
		await _api.PasskeyLoginAsync(ceremonyId, credential, rememberMe) switch
		{
			LoginResponse session => await SignedInAsync(session) switch
			{
				IReadOnlyList<CharacterSummary> characters => new PasskeyOutcome<IReadOnlyList<CharacterSummary>>(characters),
				ApiFailure failure => failure,
			},
			ApiFailure failure => Logged(failure, "Passkey login"),
		};

	/// <summary>Creates an account and signs in to it; answers with its (empty) roster.</summary>
	public async Task<ApiResult<IReadOnlyList<CharacterSummary>>> RegisterAsync(
		string username, string? email, string password, bool rememberMe = false) =>
		await _api.RegisterAsync(username, email, password, rememberMe) switch
		{
			LoginResponse session => await SignedInAsync(session),
			ApiFailure failure => Logged(failure, "Account registration"),
		};

	/// <summary>
	/// Adopts a session the server minted for a sign-in. A session this tab cannot keep is refused
	/// outright, so the tab never runs as an account a reload would not bring back.
	/// </summary>
	private async Task<ApiResult<IReadOnlyList<CharacterSummary>>> SignedInAsync(LoginResponse session)
	{
		if (!await TryPersistSessionAsync(session.AccountSessionToken, session.Username, session.MustChangePassword, session.Role, session.Permissions))
			return SessionNotSaved();
		_roster.SetRoster(session.Characters);
		return new ApiResult<IReadOnlyList<CharacterSummary>>(session.Characters);
	}

	/// <summary>
	/// Whether the game still needs first-run setup, or the <see cref="ApiFailure"/> that stopped the
	/// question being answered.
	/// </summary>
	/// <remarks>
	/// The failed arm is load-bearing, not decoration: a transient error mistaken for "setup already
	/// done" permanently hides the first-run wizard for the session, which is the bug this shape
	/// exists to prevent. "We could not ask" is a third answer a caller has to handle, and a null that
	/// a caller may silently coalesce to false is exactly how the original defect got in. A body that
	/// is not JSON at all — the SPA fallback page, from a server without the route — is a failure too.
	/// </remarks>
	public async Task<ApiResult<bool>> NeedsSetupAsync() =>
		await _api.SetupStatusAsync() switch
		{
			SetupStatusResponse status => status.NeedsSetup,
			ApiFailure failure => Logged(failure, "Setup status check"),
		};

	/// <summary>
	/// Claims the pre-generated administrator. The claim and the automatic sign-in are reported
	/// separately; see <see cref="SetupClaimed"/>.
	/// </summary>
	public async Task<ApiResult<SetupClaimed>> CompleteSetupAsync(string username, string password) =>
		await _api.CompleteSetupAsync(username, password) switch
		{
			LoginResponse session => new SetupClaimed(await TrySignInAfterClaimAsync(session)),
			ApiFailure failure => Logged(failure, "Setup completion"),
		};

	private async Task<bool> TrySignInAfterClaimAsync(LoginResponse session)
	{
		// The claim itself succeeded whenever we get here. api/setup/complete normally mints a session
		// exactly like account-login (auto-login as the new administrator) — but if post-claim
		// enrichment failed server-side, it degrades to an empty token instead of a 500 so the claim
		// isn't lost. Don't persist an empty/missing session, or one missing its name or roster: that
		// would leave IsLoggedIn true with a session the tab cannot use.
		if (!AccountApiClient.IsComplete(session))
			return false;

		// Same for a session this tab cannot store: the claim stands, only the automatic sign-in is lost.
		if (!await TryPersistSessionAsync(session.AccountSessionToken, session.Username, session.MustChangePassword, session.Role, session.Permissions))
			return false;
		_roster.SetRoster(session.Characters);
		return true;
	}

	/// <summary>
	/// Development-only: get a debug OTT for player #1 without credentials.
	/// The server endpoint is only active when DebugAuth is enabled (development mode).
	///
	/// Single-flight, idempotent, and cached for the app lifetime once it succeeds: at boot,
	/// MainLayout, GlobalTerminal, Account.razor, and DebugAuthStateProvider can all call this
	/// concurrently, and every caller (that one and any later one) shares the very same in-flight
	/// or completed task instead of each minting its own OTT. This matters because the returned
	/// token is SINGLE-USE — minting four separate tokens for one boot meant three of them were
	/// dead on arrival and only whichever the terminal happened to redeem actually worked. Sharing
	/// one response across callers is correct today because only terminal-connect paths redeem the
	/// token, and they guard on <c>Terminal.IsConnected</c> first; any future caller that wants to
	/// redeem this token must add an equivalent guard before doing so, since a second redemption
	/// attempt will fail server-side.
	/// </summary>
	public Task<DebugOttResponse?> GetDebugOttAsync() => _debugOttTask ??= GetDebugOttCoreAsync();

	/// <summary>
	/// Drops the cached debug OTT. The token is single-use server-side but cached for the app
	/// lifetime, so a recreated terminal in dev cannot reuse it — the next caller must mint a fresh one.
	/// </summary>
	public void InvalidateDebugOtt() => _debugOttTask = null;

	/// <summary>Whether this tab signed in through the development debug OTT.</summary>
	public bool HasDebugOtt => _debugOttTask is not null;

	private async Task<DebugOttResponse?> GetDebugOttCoreAsync()
	{
		// Force a genuine suspension before touching any state, for exactly the reentrancy reason
		// documented on InitCoreAsync above: TryPersistSessionAsync below can synchronously fire
		// AuthStateChanged, which DebugAuthStateProvider handles by calling straight back into
		// GetDebugOttAsync(). If every await in this method happened to complete synchronously (as
		// a test fake IJSRuntime/HttpMessageHandler can), the whole method body — including that
		// re-entrant call — would run before the `_debugOttTask ??= GetDebugOttCoreAsync()`
		// assignment in GetDebugOttAsync() ever lands, so the re-entrant call would see a still-null
		// _debugOttTask and kick off a second, duplicate debug-OTT fetch — the very bug this method
		// exists to prevent. Yielding here guarantees this method's Task is cached in _debugOttTask
		// before any of the body (or its re-entrant fallout) executes.
		await Task.Yield();

		// Hydrate first: CascadingAuthenticationState (App.razor root) can call through to this
		// method (via DebugAuthStateProvider) before any component has called InitAsync — on a
		// page refresh there is no guaranteed ordering. Without this, ExplicitlyLoggedOut would
		// still be the un-hydrated default `false` below, and a real logout wouldn't survive
		// the next reload.
		await InitAsync();

		// Chokepoint for the explicit-logout latch: DebugAuthStateProvider.GetAuthenticationStateAsync
		// is called on every auth-state query (every F5 / CascadingAuthenticationState evaluation), so
		// without this guard HERE, that routine re-auth would call through to TryPersistSessionAsync below
		// and silently clear ExplicitlyLoggedOut, undoing an explicit logout on the very next reload.
		if (ExplicitlyLoggedOut)
		{
			// Don't leave a cached null latched forever: once a later login clears
			// ExplicitlyLoggedOut, the next call must re-evaluate (and re-fetch) rather than keep
			// replaying this same completed null task.
			_debugOttTask = null;
			return null;
		}

		if (await _api.DebugOttAsync() is not DebugOttResponse result)
		{
			// Server unreachable (or the endpoint absent) this time doesn't mean it always will be —
			// clear so a later call retries.
			logger.LogWarning("Debug OTT request failed");
			_debugOttTask = null;
			return null;
		}

		if (result.AccountSessionToken is not null && result.AccountUsername is not null
			&& !await TryPersistSessionAsync(result.AccountSessionToken, result.AccountUsername, result.AccountMustChangePassword, role: null, permissions: null))
		{
			_debugOttTask = null;
			return null;
		}

		// Only a successful response is cached for the app lifetime; _debugOttTask stays set.
		return result;
	}

	/// <summary>
	/// Exchange the account session + a character for a single-use MUSH login token.
	/// </summary>
	public async Task<ApiResult<string>> GetOttForCharacterAsync(CharacterSummary character)
	{
		// AccountSessionToken is only populated by InitAsync/TryPersistSessionAsync; hydrate first so
		// a pre-init caller doesn't misread a real stored session as "not logged in".
		await InitAsync();
		if (AccountSessionToken is not { } session) return NotLoggedIn(NotLoggedInMessage);

		return await _api.MushTokenAsync(session, character) switch
		{
			MushTokenResponse token => token.Token,
			// The session rides in the body as well as the header, so the bearer handler's retry still
			// names the refused one: ask again with whatever the tab renewed to.
			ApiFailure { Kind: ApiFailureKind.Unauthenticated }
				when await RenewSessionAsync(session) is { } renewed => await _api.MushTokenAsync(renewed, character) switch
				{
					MushTokenResponse token => token.Token,
					ApiFailure retried => Logged(retried, "OTT via account session"),
				},
			ApiFailure failure => Logged(failure, "OTT via account session"),
		};
	}

	/// <summary>
	/// Switch the active character under the current account session and return an OTT for it.
	/// Calls <c>POST api/auth/switch-character</c>, the session-based replacement for the retired
	/// <c>jwt-switch-character</c> flow: the same account session stays active — this
	/// mints no new token family, just a fresh single-use OTT for the target character.
	/// </summary>
	public async Task<ApiResult<string>> SwitchCharacterAsync(CharacterSummary character)
	{
		// AccountSessionToken is only populated by InitAsync/TryPersistSessionAsync; hydrate first so
		// a pre-init caller doesn't misread a real stored session as "not logged in".
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn(NotLoggedInMessage);

		return await _api.SwitchCharacterAsync(character) switch
		{
			SwitchCharacterResponse { Ott: not null, AccountSessionToken: { Length: > 0 } } switched
				=> await SwitchedAsync(character, switched),
			SwitchCharacterResponse => Logged(
				new ApiFailure(ApiFailureKind.Unexpected, "The server returned no token for that character."), "Switch character"),
			ApiFailure failure => Logged(failure, "Switch character"),
		};
	}

	private async Task<ApiResult<string>> SwitchedAsync(CharacterSummary character, SwitchCharacterResponse switched)
	{
		// Adopt the freshly minted token: it is bound to the target character server-side, so from
		// here on every request — including after a reload, since sessionStorage is tab-scoped —
		// is that character without the client asserting anything. The old token is left to lapse
		// on its own TTL rather than revoked, because a tab opened from this one may still hold a
		// copy of it.
		if (!await TryAdoptSessionTokenAsync(switched.AccountSessionToken))
			return Logged(SessionNotSaved(), "Switch character");

		SetActiveCharacter(character);
		return switched.Ott;
	}

	/// <summary>
	/// This account's roster, or the <see cref="ApiFailure"/> that stopped the read. An anonymous tab
	/// is the empty roster, not a failure — there is nothing to ask for and no session to ask with.
	/// </summary>
	/// <remarks>
	/// "This account owns no character" and "we could not find out" are different facts. A failed
	/// request that degraded to an empty list would have /play tell an account whose roster request
	/// had merely failed that it had no character, and offer to create one. The failure is in the
	/// type so a consumer has to decide what to do with it.
	/// </remarks>
	/// <para>
	/// MainLayout, the global terminal and the quickstart widget each ask on first render; their
	/// "roster still empty" guards all pass before any answer lands, so concurrent calls on one
	/// session share a request. A call after it finishes (after a mutation, say) asks again.
	/// </para>
	public async Task<ApiResult<IReadOnlyList<CharacterSummary>>> GetCharactersAsync()
	{
		await InitAsync();
		if (AccountSessionToken is not { } session) return new ApiResult<IReadOnlyList<CharacterSummary>>([]);

		return await _charactersFlight.RunAsync(session, FetchCharactersAsync);
	}

	private async Task<ApiResult<IReadOnlyList<CharacterSummary>>> FetchCharactersAsync() =>
		await _api.CharactersAsync() switch
		{
			IReadOnlyList<CharacterSummary> characters => Roster(characters),
			ApiFailure failure => Logged(failure, "GetCharacters"),
		};

	private ApiResult<IReadOnlyList<CharacterSummary>> Roster(IReadOnlyList<CharacterSummary> characters)
	{
		_roster.SetRoster(characters);
		return new ApiResult<IReadOnlyList<CharacterSummary>>(Characters);
	}

	/// <summary>Creates a character on this account; it joins the roster without becoming the acting one.</summary>
	public async Task<ApiResult<CharacterSummary>> CreateCharacterAsync(string name, string password)
	{
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn("Not logged in to account.");

		return await _api.CreateCharacterAsync(name, password) switch
		{
			CreateCharacterResponse created => await AddedAsync(new CharacterSummary(created.DbrefNumber, created.CreationTime ?? 0, name, created.Flags ?? "")),
			ApiFailure failure => Logged(failure, "CreateCharacter"),
		};
	}

	/// <summary>
	/// Links a character that already exists (made at the connect screen, by staff, or imported) to this
	/// account, proven by its password. Like a new one, it joins the roster without becoming the acting one.
	/// </summary>
	public async Task<ApiResult<CharacterSummary>> ClaimCharacterAsync(string name, string password)
	{
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn("Not logged in to account.");

		return await _api.ClaimCharacterAsync(name, password) switch
		{
			ClaimCharacterResponse claimed => await AddedAsync(new CharacterSummary(claimed.DbrefNumber, claimed.CreationTime, claimed.Name, claimed.Flags ?? "")),
			ApiFailure failure => Logged(failure, "ClaimCharacter"),
		};
	}

	private async Task<CharacterSummary> AddedAsync(CharacterSummary character)
	{
		// Claiming a character the account already holds succeeds without linking anything new.
		if (!Characters.Any(c => c.DbrefNumber == character.DbrefNumber))
			_roster.Add(character);
		// An account's role comes from its characters: a fresh account is a Guest until its first
		// one exists, and stayed one in this tab (no build tools, no wiki editing) until it signed in again.
		await ReloadAuthorityAsync();
		return character;
	}

	/// <summary>Stores one of the account's characters' portal theme and accent, and updates the roster to match.</summary>
	public async Task<ApiResult<CharacterAppearance>> SetAppearanceAsync(int dbrefNumber, CharacterAppearance appearance)
	{
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn("Not logged in to account.");

		var result = await _api.SetAppearanceAsync(dbrefNumber, appearance);
		if (result is CharacterAppearance stored)
		{
			_roster.SetAppearance(dbrefNumber, stored);
		}

		return result;
	}

	/// <summary>Unlinks a character from this account; see <see cref="CharacterUnlinked"/> for the advisory.</summary>
	public async Task<ApiResult<CharacterUnlinked>> UnlinkCharacterAsync(int dbrefNumber)
	{
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn("Not logged in to account.");

		return await _api.UnlinkCharacterAsync(dbrefNumber) switch
		{
			Success => await UnlinkedAsync(dbrefNumber),
			ApiFailure failure => Logged(failure, "UnlinkCharacter"),
		};
	}

	private async Task<CharacterUnlinked> UnlinkedAsync(int dbrefNumber)
	{
		var unlinkedTheActing = ActiveCharacter?.DbrefNumber == dbrefNumber;
		var remaining = _roster.Without(dbrefNumber);

		// Unlinking the character this tab acts as leaves the session bound to a character the
		// account no longer owns, which the server treats as acting-as-nobody. Rebind to a
		// remaining character so the tab comes back with a usable identity instead of a dead one;
		// switching is the only thing that can, since only the server may mint the binding.
		if (unlinkedTheActing && remaining.FirstOrDefault() is { } replacement)
		{
			var rebound = await SwitchCharacterAsync(replacement) is string;

			// Re-read the roster either way: the unlink itself succeeded, so the list must reflect
			// it, and the server is the only thing that can say what the session is bound to now.
			await GetCharactersAsync();
			await ReloadAuthorityAsync();

			if (!rebound)
			{
				// The unlink stands, but this tab is acting as nobody until something switches it.
				// Reported rather than swallowed — the caller cannot tell "rebound to a fresh
				// character" from "left with no identity" otherwise.
				logger.LogWarning("Unlinked the acting character but could not rebind the session; acting as nobody until the next switch");
				return new CharacterUnlinked("Character unlinked, but switching to another character failed. Pick a character to continue.");
			}

			return new CharacterUnlinked(Advisory: null);
		}

		_roster.SetRoster(remaining);
		// The role the unlinked character gave the account goes with it.
		await ReloadAuthorityAsync();
		return new CharacterUnlinked(Advisory: null);
	}

	public async Task<ApiResult<Success>> ChangePasswordAsync(string oldPassword, string newPassword)
	{
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn(NotLoggedInMessage);

		return await _api.ChangePasswordAsync(oldPassword, newPassword) switch
		{
			Success => await PasswordChangedAsync(),
			ApiFailure failure => Logged(failure, "Change password"),
		};
	}

	private async Task<Success> PasswordChangedAsync()
	{
		MustChangePassword = false;
		await _storage.WriteMustChangePasswordAsync(false);
		return new Success();
	}

	public async Task<ApiResult<Success>> ChangeEmailAsync(string? newEmail, string currentPassword)
	{
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn(NotLoggedInMessage);

		return await _api.ChangeEmailAsync(newEmail, currentPassword) switch
		{
			Success success => success,
			ApiFailure failure => Logged(failure, "Change email"),
		};
	}

	public async Task<ApiResult<Success>> ChangeUsernameAsync(string newUsername)
	{
		await InitAsync();
		if (AccountSessionToken is null) return NotLoggedIn(NotLoggedInMessage);

		return await _api.ChangeUsernameAsync(newUsername) switch
		{
			Success => await RenamedAsync(newUsername),
			ApiFailure failure => Logged(failure, "Change username"),
		};
	}

	private async Task<Success> RenamedAsync(string newUsername)
	{
		Username = newUsername;
		await _storage.WriteUsernameAsync(newUsername);
		return new Success();
	}

	public async Task LogoutAsync()
	{
		// Hydrate first: a not-yet-inited service could otherwise treat a real stored session as
		// already logged out, skip the server-side logout call, but still latch ExplicitlyLoggedOut
		// and wipe storage under the caller's feet.
		await InitAsync();

		// Best-effort: the server-side session lapses on its own TTL if this does not land, and the
		// local sign-out below must happen either way.
		if (AccountSessionToken is not null)
			await _api.LogoutAsync();

		// Finish whatever the session was holding open — the game-side terminals above all — while
		// there is still a session to finish it with. Logout is the single chokepoint every entry
		// point routes through, so nothing registered here can be forgotten by a caller.
		foreach (var handler in sessionEndingHandlers)
		{
			try
			{
				await handler.OnAccountSessionEndingAsync();
			}
			catch (Exception ex)
			{
				// Ending the local session must not depend on a best-effort cleanup succeeding; the
				// alternative is a tab that failed to hang up a socket and is also still signed in.
				logger.LogError(ex, "Account session-ending handler {Handler} threw", handler.GetType().Name);
			}
		}

		_authorityGeneration++;
		AccountSessionToken = null;
		Username = null;
		_roster.SetRoster([]);
		MustChangePassword = false;
		Role = null;
		Permissions = [];
		// A fresh intentional login later must mint (and redeem) its own token, not resurrect the
		// previous boot's cached debug-OTT response.
		_debugOttTask = null;

		// Explicit-logout latch: sticks until the next successful login/register/setup in this
		// tab, so dev-mode debug re-auth (or any other silent re-persist) can't undo the logout.
		ExplicitlyLoggedOut = true;
		await _storage.ClearAndLatchLoggedOutAsync();

		// Every storage mutation above (session/role/permission removal and the loggedOut latch
		// write) is complete before the event fires. This ordering is load-bearing: the event
		// synchronously drives subscriber re-renders (AccountAuthStateProvider -> MainLayout ->
		// Account.razor etc.), and a subscriber's render exception must never be able to unwind
		// back through this method and skip the persistence above.
		RaiseAuthStateChanged();
	}

	/// <summary>
	/// Replaces this tab's session token with one the server minted. Only the token changes — the
	/// account identity behind it is the same, so username/role/permissions are left alone.
	/// </summary>
	/// <returns>False, with nothing adopted, when this tab could not store the token.</returns>
	private async Task<bool> TryAdoptSessionTokenAsync(string token)
	{
		// Storage first. A write that fails leaves the in-memory token where it was, and the caller
		// reports the switch as failed; otherwise the tab would be acting as the new character while
		// the caller believes it isn't, and a reload would restore the old one.
		if (!await _storage.TryWriteTokenAsync(token))
			return false;
		AccountSessionToken = token;
		return true;
	}

	/// <summary>
	/// Persists a freshly minted session, then adopts it. Storage first for the reason
	/// <see cref="TryAdoptSessionTokenAsync"/> gives: a session this tab cannot keep is refused
	/// outright, so the tab never runs as an account a reload would not bring back.
	/// </summary>
	/// <returns>False, with nothing adopted, when this tab could not store the session.</returns>
	private async Task<bool> TryPersistSessionAsync(
		string token, string username, bool mustChangePassword, string? role, IReadOnlyList<string>? permissions)
	{
		permissions ??= [];
		if (!await _storage.TryWriteAsync(token, username, mustChangePassword, role, permissions))
			return false;

		_authorityGeneration++;
		AccountSessionToken = token;
		Username = username;
		MustChangePassword = mustChangePassword;
		Role = role;
		Permissions = permissions;
		ExplicitlyLoggedOut = false;
		RaiseAuthStateChanged();
		return true;
	}

	/// <summary>
	/// Raises <see cref="AuthStateChanged"/> defensively: a subscriber's render exception (e.g. a
	/// component crashing mid-re-render) must never propagate back into the caller — that would
	/// abort whatever the caller does next (e.g. <see cref="LogoutAsync"/>'s callers resetting UI
	/// state and navigating away). Logged and swallowed instead.
	/// </summary>
	private void RaiseAuthStateChanged()
	{
		try
		{
			AuthStateChanged?.Invoke();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "AuthStateChanged subscriber threw");
		}
	}

	private static ApiFailure NotLoggedIn(string message) => new(ApiFailureKind.Unauthenticated, message);

	private static ApiFailure SessionNotSaved() => new(ApiFailureKind.Unexpected, SessionNotSavedMessage);

	private ApiFailure Logged(ApiFailure failure, string operation)
	{
		logger.LogWarning("{Operation} failed: {Message}", operation, failure.Message);
		return failure;
	}
}
