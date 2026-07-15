using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side service for Account-based authentication and management.
/// Stores the account session token and display name in sessionStorage
/// (tab-scoped — cleared when the browser tab is closed).
/// Passwords are never stored.
/// </summary>
public class AccountAuthService(
	IHttpClientFactory httpClientFactory,
	IJSRuntime js,
	ILogger<AccountAuthService> logger) : IAccountAuthState
{
	private const string SessionTokenKey = "sharpmush.account.sessionToken";
	private const string UsernameKey = "sharpmush.account.username";
	private const string MustChangePasswordKey = "sharpmush.account.mustChangePassword";
	private const string RoleKey = "sharpmush.account.role";
	private const string PermissionsKey = "sharpmush.account.permissions";
	private const string LoggedOutKey = "sharpmush.account.loggedOut";

	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);

	private record AccountLoginRequest(string UsernameOrEmail, string Password);
	private record AccountRegisterRequest(string Username, string? Email, string Password);
	private record AccountLoginResponse(
		string AccountId,
		string Username,
		IReadOnlyList<CharacterSummary> Characters,
		string AccountSessionToken,
		bool MustChangePassword,
		string? Role,
		IReadOnlyList<string>? Permissions);
	private record MushTokenWithAccountRequest(string AccountSessionToken, int CharacterKey, long CharacterCreationTime);
	private record MushTokenResponse(string Token, int ExpiresIn);
	public record DebugOttResponse(string Token, int ExpiresIn, string PlayerName,
		string? AccountId, string? AccountUsername, string? AccountSessionToken, bool AccountMustChangePassword);
	private record CreateCharacterRequest(string Name, string Password);
	private record CreateCharacterResponse(int DbrefNumber, long? CreationTime);
	private record ChangePasswordRequest(string OldPassword, string NewPassword);
	private record ChangeEmailRequest(string? NewEmail, string CurrentPassword);
	private record ChangeUsernameRequest(string NewUsername);
	private record SetupStatusResponse(bool NeedsSetup);
	private record SetupCompleteRequest(string Username, string Password);

	public string? AccountSessionToken { get; private set; }
	public string? Username { get; private set; }
	public IReadOnlyList<CharacterSummary> Characters { get; private set; } = [];
	public bool MustChangePassword { get; private set; }
	public bool IsLoggedIn => AccountSessionToken is not null;
	public string? Role { get; private set; }
	public IReadOnlyList<string> Permissions { get; private set; } = [];

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

		var loggedOutFlag = await js.InvokeAsync<string?>("sessionStorage.getItem", LoggedOutKey);
		ExplicitlyLoggedOut = string.Equals(loggedOutFlag, bool.TrueString, StringComparison.OrdinalIgnoreCase);

		AccountSessionToken = await js.InvokeAsync<string?>("sessionStorage.getItem", SessionTokenKey);
		if (AccountSessionToken is null)
		{
			// No session in this tab (sessionStorage is tab-scoped): don't restore Username/Role/
			// Permissions from localStorage/sessionStorage — a returning user in a new tab would
			// otherwise get a phantom identity with no live session. Nothing in the portal
			// pre-fills the login form from Username, so there's no UX reason to keep it around.
			Username = null;
			MustChangePassword = false;
			Role = null;
			Permissions = [];
			RaiseAuthStateChanged();
			return;
		}

		Username = await js.InvokeAsync<string?>("localStorage.getItem", UsernameKey);
		var mustChangePassword = await js.InvokeAsync<string?>("sessionStorage.getItem", MustChangePasswordKey);
		MustChangePassword = string.Equals(mustChangePassword, bool.TrueString, StringComparison.OrdinalIgnoreCase);
		Role = await js.InvokeAsync<string?>("sessionStorage.getItem", RoleKey);
		var permissionsJson = await js.InvokeAsync<string?>("sessionStorage.getItem", PermissionsKey);
		Permissions = permissionsJson is null
			? []
			: JsonSerializer.Deserialize<IReadOnlyList<string>>(permissionsJson) ?? [];
		// CascadingAuthenticationState snapshots before MainLayout's InitAsync runs; re-notify so a reloaded tab's restored session reaches [Authorize] gates.
		RaiseAuthStateChanged();
	}

	public async Task<(bool Success, string? Error, IReadOnlyList<CharacterSummary> Characters)> LoginAsync(
		string identifier, string password)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/auth/account-login",
				new AccountLoginRequest(identifier, password));

			if (!response.IsSuccessStatusCode)
				return (false, await response.Content.ReadAsStringAsync(), []);

			var result = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
			if (result is null) return (false, "Unexpected server response.", []);

			await PersistSessionAsync(result.AccountSessionToken, result.Username, result.MustChangePassword, result.Role, result.Permissions);
			Characters = result.Characters;
			return (true, null, result.Characters);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Account login failed");
			return (false, ex.Message, []);
		}
	}

	public async Task<(bool Success, string? Error, IReadOnlyList<CharacterSummary> Characters)> RegisterAsync(
		string username, string? email, string password)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/auth/account-register",
				new AccountRegisterRequest(username, string.IsNullOrWhiteSpace(email) ? null : email, password));

			if (!response.IsSuccessStatusCode)
				return (false, await response.Content.ReadAsStringAsync(), []);

			var result = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
			if (result is null) return (false, "Unexpected server response.", []);

			await PersistSessionAsync(result.AccountSessionToken, result.Username, result.MustChangePassword, result.Role, result.Permissions);
			Characters = result.Characters;
			return (true, null, result.Characters);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Account registration failed");
			return (false, ex.Message, []);
		}
	}

	/// <summary>
	/// Checks whether the game still needs first-run setup.
	/// Returns <c>null</c> (rather than a false negative) on any failure to reach or parse
	/// the server response — a transient error must never be mistaken for "setup already
	/// done," since that would permanently hide the first-run wizard for the session.
	/// </summary>
	public async Task<bool?> NeedsSetupAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync("api/setup/status");
			if (!response.IsSuccessStatusCode)
			{
				logger.LogError("Setup status check returned {Status}", response.StatusCode);
				return null;
			}

			var result = await response.Content.ReadFromJsonAsync<SetupStatusResponse>();
			if (result is null)
			{
				logger.LogError("Setup status check returned an unparseable response");
				return null;
			}

			return result.NeedsSetup;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to check setup status");
			return null;
		}
	}

	public async Task<(bool Success, string? Error, bool AutoLoggedIn)> CompleteSetupAsync(string username, string password)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/setup/complete",
				new SetupCompleteRequest(username, password));
			if (!response.IsSuccessStatusCode)
				return (false, await response.Content.ReadAsStringAsync(), false);

			var result = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
			if (result is null) return (false, "Unexpected server response.", false);

			// The claim itself succeeded whenever we get here. api/setup/complete normally mints
			// a session exactly like account-login (auto-login as the new administrator) — but if
			// post-claim enrichment failed server-side, it degrades to an empty token instead of a
			// 500 so the claim isn't lost. Don't persist an empty/missing session: that would leave
			// IsLoggedIn true with a token that can't authenticate anything.
			if (string.IsNullOrEmpty(result.AccountSessionToken))
				return (true, null, false);

			await PersistSessionAsync(result.AccountSessionToken, result.Username, result.MustChangePassword, result.Role, result.Permissions);
			Characters = result.Characters;
			return (true, null, true);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Setup completion failed");
			return (false, ex.Message, false);
		}
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

	private async Task<DebugOttResponse?> GetDebugOttCoreAsync()
	{
		// Force a genuine suspension before touching any state, for exactly the reentrancy reason
		// documented on InitCoreAsync above: PersistSessionAsync below can synchronously fire
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
		// without this guard HERE, that routine re-auth would call through to PersistSessionAsync below
		// and silently clear ExplicitlyLoggedOut, undoing an explicit logout on the very next reload.
		if (ExplicitlyLoggedOut)
		{
			// Don't leave a cached null latched forever: once a later login clears
			// ExplicitlyLoggedOut, the next call must re-evaluate (and re-fetch) rather than keep
			// replaying this same completed null task.
			_debugOttTask = null;
			return null;
		}

		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync("api/auth/debug-ott");
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Debug OTT request failed: {Status}", response.StatusCode);
				_debugOttTask = null;
				return null;
			}
			var result = await response.Content.ReadFromJsonAsync<DebugOttResponse>();
			if (result is null)
			{
				_debugOttTask = null;
				return null;
			}

			if (result.AccountSessionToken is not null && result.AccountUsername is not null)
				await PersistSessionAsync(result.AccountSessionToken, result.AccountUsername, result.AccountMustChangePassword, role: null, permissions: null);

			// Only a successful response is cached for the app lifetime; _debugOttTask stays set.
			return result;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Debug OTT request threw an exception");
			// Server unreachable this time doesn't mean it always will be — clear so a later call retries.
			_debugOttTask = null;
			return null;
		}
	}

	/// <summary>
	/// Exchange an account session token + character selection for a MUSH OTT.
	/// </summary>
	public async Task<string?> GetOttForCharacterAsync(CharacterSummary character)
	{
		// AccountSessionToken is only populated by InitAsync/PersistSessionAsync; hydrate first so
		// a pre-init caller doesn't misread a real stored session as "not logged in".
		await InitAsync();
		if (AccountSessionToken is null) return null;

		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/auth/mush-token",
				new MushTokenWithAccountRequest(AccountSessionToken, character.DbrefNumber, character.CreationTime));

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("OTT via account session failed: {Status}", response.StatusCode);
				return null;
			}

			var result = await response.Content.ReadFromJsonAsync<MushTokenResponse>();
			return result?.Token;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "OTT via account session threw an exception");
			return null;
		}
	}

	public async Task<IReadOnlyList<CharacterSummary>> GetCharactersAsync()
	{
		await InitAsync();
		if (AccountSessionToken is null) return [];

		try
		{
			var http = httpClientFactory.CreateClient("api");
			http.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccountSessionToken);
			var characters = await http.GetFromJsonAsync<IReadOnlyList<CharacterSummary>>("api/account/characters");
			Characters = characters ?? [];
			return Characters;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetCharacters failed");
			return [];
		}
	}

	public async Task<(bool Success, string? Error, CharacterSummary? Character)> CreateCharacterAsync(
		string name, string password)
	{
		await InitAsync();
		if (AccountSessionToken is null) return (false, "Not logged in to account.", null);

		try
		{
			var http = httpClientFactory.CreateClient("api");
			http.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccountSessionToken);
			var response = await http.PostAsJsonAsync("api/account/characters",
				new CreateCharacterRequest(name, password));

			if (!response.IsSuccessStatusCode)
				return (false, await response.Content.ReadAsStringAsync(), null);

			var result = await response.Content.ReadFromJsonAsync<CreateCharacterResponse>();
			if (result is null) return (false, "Unexpected server response.", null);

			var character = new CharacterSummary(result.DbrefNumber, result.CreationTime ?? 0, name, "");
			Characters = [.. Characters, character];
			return (true, null, character);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "CreateCharacter failed");
			return (false, ex.Message, null);
		}
	}

	public async Task<(bool Success, string? Error)> UnlinkCharacterAsync(int dbrefNumber)
	{
		await InitAsync();
		if (AccountSessionToken is null) return (false, "Not logged in to account.");

		try
		{
			var http = httpClientFactory.CreateClient("api");
			http.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccountSessionToken);
			var response = await http.DeleteAsync($"api/account/characters/{dbrefNumber}");
			if (!response.IsSuccessStatusCode)
				return (false, await response.Content.ReadAsStringAsync());

			Characters = Characters.Where(c => c.DbrefNumber != dbrefNumber).ToList();
			return (true, null);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "UnlinkCharacter failed");
			return (false, ex.Message);
		}
	}

	public async Task<(bool Success, string? Error)> ChangePasswordAsync(string oldPassword, string newPassword)
	{
		await InitAsync();
		if (AccountSessionToken is null) return (false, "Not logged in.");
		var (success, error) = await PutAsync("api/account/password", new ChangePasswordRequest(oldPassword, newPassword));
		if (success)
			await SetMustChangePasswordAsync(false);
		return (success, error);
	}

	public async Task<(bool Success, string? Error)> ChangeEmailAsync(string? newEmail, string currentPassword)
	{
		await InitAsync();
		if (AccountSessionToken is null) return (false, "Not logged in.");
		return await PutAsync("api/account/email", new ChangeEmailRequest(newEmail, currentPassword));
	}

	public async Task<(bool Success, string? Error)> ChangeUsernameAsync(string newUsername)
	{
		await InitAsync();
		if (AccountSessionToken is null) return (false, "Not logged in.");
		var (success, error) = await PutAsync("api/account/username", new ChangeUsernameRequest(newUsername));
		if (success)
		{
			Username = newUsername;
			await js.InvokeVoidAsync("localStorage.setItem", UsernameKey, newUsername);
		}
		return (success, error);
	}

	public async Task LogoutAsync()
	{
		// Hydrate first: a not-yet-inited service could otherwise treat a real stored session as
		// already logged out, skip the server-side logout call, but still latch ExplicitlyLoggedOut
		// and wipe storage under the caller's feet.
		await InitAsync();
		if (AccountSessionToken is not null)
		{
			try
			{
				var http = httpClientFactory.CreateClient("api");
				http.DefaultRequestHeaders.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccountSessionToken);
				await http.PostAsync("api/account/logout", null);
			}
			catch { /* best-effort */ }
		}

		AccountSessionToken = null;
		Username = null;
		Characters = [];
		MustChangePassword = false;
		Role = null;
		Permissions = [];
		// A fresh intentional login later must mint (and redeem) its own token, not resurrect the
		// previous boot's cached debug-OTT response.
		_debugOttTask = null;
		await js.InvokeVoidAsync("sessionStorage.removeItem", SessionTokenKey);
		await js.InvokeVoidAsync("sessionStorage.removeItem", MustChangePasswordKey);
		await js.InvokeVoidAsync("sessionStorage.removeItem", RoleKey);
		await js.InvokeVoidAsync("sessionStorage.removeItem", PermissionsKey);

		// Explicit-logout latch: sticks until the next successful login/register/setup in this
		// tab, so dev-mode debug re-auth (or any other silent re-persist) can't undo the logout.
		ExplicitlyLoggedOut = true;
		await js.InvokeVoidAsync("sessionStorage.setItem", LoggedOutKey, bool.TrueString);

		// Every storage mutation above (session/role/permission removal and the loggedOut latch
		// write) is complete before the event fires. This ordering is load-bearing: the event
		// synchronously drives subscriber re-renders (AccountAuthStateProvider -> MainLayout ->
		// Account.razor etc.), and a subscriber's render exception must never be able to unwind
		// back through this method and skip the persistence above.
		RaiseAuthStateChanged();
	}

	private async Task PersistSessionAsync(
		string token, string username, bool mustChangePassword, string? role, IReadOnlyList<string>? permissions)
	{
		AccountSessionToken = token;
		Username = username;
		Role = role;
		Permissions = permissions ?? [];
		await js.InvokeVoidAsync("sessionStorage.setItem", SessionTokenKey, token);
		await js.InvokeVoidAsync("localStorage.setItem", UsernameKey, username);
		await SetMustChangePasswordAsync(mustChangePassword);
		if (role is null)
			await js.InvokeVoidAsync("sessionStorage.removeItem", RoleKey);
		else
			await js.InvokeVoidAsync("sessionStorage.setItem", RoleKey, role);
		await js.InvokeVoidAsync("sessionStorage.setItem", PermissionsKey, JsonSerializer.Serialize(Permissions));

		// Any successful login/register/setup clears a prior explicit logout.
		ExplicitlyLoggedOut = false;
		await js.InvokeVoidAsync("sessionStorage.removeItem", LoggedOutKey);
		RaiseAuthStateChanged();
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

	private async Task SetMustChangePasswordAsync(bool value)
	{
		MustChangePassword = value;
		await js.InvokeVoidAsync("sessionStorage.setItem", MustChangePasswordKey, value.ToString());
	}

	private async Task<(bool Success, string? Error)> PutAsync<T>(string path, T body)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			http.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccountSessionToken);
			var response = await http.PutAsJsonAsync(path, body);
			if (!response.IsSuccessStatusCode)
				return (false, await response.Content.ReadAsStringAsync());
			return (true, null);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "PUT {Path} failed", path);
			return (false, ex.Message);
		}
	}
}
