using System.Net.Http.Headers;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using static SharpMUSH.Client.Services.AccountAuthService;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The account and session endpoints as typed calls: the wire records and one <see cref="ApiCall"/>
/// per route, holding no state.
/// </summary>
/// <remarks>
/// <para>Split out of <see cref="AccountAuthService"/>, which used to carry these fourteen records and
/// spell out each request itself, answering with a <c>(Success, Error, …)</c> tuple whose error was the
/// raw response body. Every call here answers with an <see cref="ApiResult{T}"/> instead, so a refusal
/// arrives as the server's sentence and an outage as an outage — the same contract the other typed
/// clients under <c>Services/</c> keep.</para>
///
/// <para>Deciding what an answer means for the tab — adopting a token, reseating the active character,
/// latching a sign-out — stays with <see cref="AccountAuthService"/>. Nothing here sets an
/// <c>Authorization</c> header except <see cref="SessionAsync"/>, whose reason is on it.</para>
/// </remarks>
public sealed class AccountApiClient(IHttpClientFactory httpClientFactory)
{
	public sealed record LoginResponse(
		string AccountId,
		string Username,
		IReadOnlyList<CharacterSummary> Characters,
		string AccountSessionToken,
		bool MustChangePassword,
		string? Role,
		IReadOnlyList<string>? Permissions);

	public sealed record SessionStateResponse(
		string Username, bool MustChangePassword, string? Role, IReadOnlyList<string>? Permissions);

	public sealed record MushTokenResponse(string Token, int ExpiresIn);

	public sealed record SwitchCharacterResponse(string Ott, int ExpiresIn, string AccountSessionToken);

	/// <param name="Flags">The new character's flags, as the roster carries them.</param>
	public sealed record CreateCharacterResponse(int DbrefNumber, long? CreationTime, string? Flags = null);

	public sealed record SetupStatusResponse(bool NeedsSetup);

	private sealed record LoginRequest(string UsernameOrEmail, string Password);
	private sealed record RegisterRequest(string Username, string? Email, string Password);
	private sealed record SetupCompleteRequest(string Username, string Password);
	private sealed record MushTokenRequest(string AccountSessionToken, int CharacterKey, long CharacterCreationTime);
	private sealed record SwitchCharacterRequest(int CharacterKey, long CharacterCreationTime);
	private sealed record CreateCharacterRequest(string Name, string Password);
	private sealed record ChangePasswordRequest(string OldPassword, string NewPassword);
	private sealed record ChangeEmailRequest(string? NewEmail, string CurrentPassword);
	private sealed record ChangeUsernameRequest(string NewUsername);
	private sealed record PasskeyLoginRequest(string CeremonyId, JsonElement Credential);
	private sealed record PasskeyOptionsRequest(string CurrentPassword);
	private sealed record AddPasskeyRequest(string CeremonyId, string? Name, JsonElement Credential);
	private sealed record RenamePasskeyRequest(string Name);

	/// <summary>A passkey ceremony the server started: the options for the browser, and the id to answer with.</summary>
	public sealed record PasskeyChallenge(string CeremonyId, JsonElement Options);

	/// <summary>One of the account's passkeys. <paramref name="IsSynced"/>: the authenticator keeps it on more than one device.</summary>
	public sealed record PasskeySummary(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool IsSynced);

	private const string NoSession = "The server answered without a session.";
	private const string IncompleteSession = "The server's sign-in answer was missing its session, name or roster.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	public async Task<ApiResult<LoginResponse>> LoginAsync(string identifier, string password) =>
		Complete(await Client.PostApiAsync<LoginRequest, LoginResponse>(
			"api/auth/account-login", new LoginRequest(identifier, password), NoSession));

	public async Task<ApiResult<LoginResponse>> RegisterAsync(string username, string? email, string password) =>
		Complete(await Client.PostApiAsync<RegisterRequest, LoginResponse>(
			"api/auth/account-register",
			new RegisterRequest(username, string.IsNullOrWhiteSpace(email) ? null : email, password),
			NoSession));

	/// <summary>
	/// A sign-in answer the tab can adopt whole. JSON that parses but leaves out the token, the name or
	/// the roster is refused here, before anything is persisted, so a caller never holds a half-adopted
	/// session.
	/// </summary>
	private static ApiResult<LoginResponse> Complete(ApiResult<LoginResponse> result) => result switch
	{
		LoginResponse session when IsComplete(session) => session,
		LoginResponse => new ApiFailure(ApiFailureKind.Unexpected, IncompleteSession),
		ApiFailure failure => failure,
	};

	/// <summary>Whether <paramref name="session"/> carries everything a sign-in adopts.</summary>
	public static bool IsComplete(LoginResponse session) =>
		!string.IsNullOrEmpty(session.AccountSessionToken)
		&& session.Username is not null
		&& session.Characters is not null;

	public Task<ApiResult<SetupStatusResponse>> SetupStatusAsync() =>
		Client.GetApiAsync<SetupStatusResponse>("api/setup/status", "The server did not say whether setup is needed.");

	public Task<ApiResult<LoginResponse>> CompleteSetupAsync(string username, string password) =>
		Client.PostApiAsync<SetupCompleteRequest, LoginResponse>(
			"api/setup/complete", new SetupCompleteRequest(username, password), NoSession);

	/// <summary>
	/// The current role, grants and name for <paramref name="token"/>.
	/// </summary>
	/// <remarks>
	/// The one call that names its own bearer. It runs inside <see cref="AccountAuthService.InitAsync"/>,
	/// and the <c>"api"</c> client's <see cref="AccountSessionBearerHandler"/> awaits that same
	/// initialisation before attaching one — leaving the header to the handler would wait on itself.
	/// </remarks>
	public async Task<ApiResult<SessionStateResponse>> SessionAsync(string token, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/session");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return await Client.SendApiAsync<SessionStateResponse>(
			request, "The server did not describe the session.", cancellationToken);
	}

	public Task<ApiResult<DebugOttResponse>> DebugOttAsync() =>
		Client.GetApiAsync<DebugOttResponse>("api/auth/debug-ott", "The server returned no debug token.");

	public Task<ApiResult<MushTokenResponse>> MushTokenAsync(string sessionToken, CharacterSummary character) =>
		Client.PostApiAsync<MushTokenRequest, MushTokenResponse>(
			"api/auth/mush-token",
			new MushTokenRequest(sessionToken, character.DbrefNumber, character.CreationTime),
			"The server returned no login token.");

	public Task<ApiResult<SwitchCharacterResponse>> SwitchCharacterAsync(CharacterSummary character) =>
		Client.PostApiAsync<SwitchCharacterRequest, SwitchCharacterResponse>(
			"api/auth/switch-character",
			new SwitchCharacterRequest(character.DbrefNumber, character.CreationTime),
			"The server returned no token for that character.");

	public Task<ApiResult<IReadOnlyList<CharacterSummary>>> CharactersAsync() =>
		Client.GetApiAsync<IReadOnlyList<CharacterSummary>>("api/account/characters", "The server returned no character list.");

	public Task<ApiResult<CreateCharacterResponse>> CreateCharacterAsync(string name, string password) =>
		Client.PostApiAsync<CreateCharacterRequest, CreateCharacterResponse>(
			"api/account/characters", new CreateCharacterRequest(name, password),
			"The character was created but the server described nothing.");

	public Task<ApiResult<Success>> UnlinkCharacterAsync(int dbrefNumber) =>
		Client.DeleteApiAsync($"api/account/characters/{dbrefNumber}");

	public Task<ApiResult<Success>> ChangePasswordAsync(string oldPassword, string newPassword) =>
		Client.PutApiAsync("api/account/password", new ChangePasswordRequest(oldPassword, newPassword));

	public Task<ApiResult<Success>> ChangeEmailAsync(string? newEmail, string currentPassword) =>
		Client.PutApiAsync("api/account/email", new ChangeEmailRequest(newEmail, currentPassword));

	public Task<ApiResult<Success>> ChangeUsernameAsync(string newUsername) =>
		Client.PutApiAsync("api/account/username", new ChangeUsernameRequest(newUsername));

	public Task<ApiResult<PasskeyChallenge>> PasskeyLoginOptionsAsync() =>
		Client.PostApiAsync<object?, PasskeyChallenge>("api/auth/passkey-login/options", null, "The server started no passkey sign-in.");

	public async Task<ApiResult<LoginResponse>> PasskeyLoginAsync(string ceremonyId, JsonElement credential) =>
		Complete(await Client.PostApiAsync<PasskeyLoginRequest, LoginResponse>(
			"api/auth/passkey-login", new PasskeyLoginRequest(ceremonyId, credential), NoSession));

	public Task<ApiResult<IReadOnlyList<PasskeySummary>>> PasskeysAsync() =>
		Client.GetApiAsync<IReadOnlyList<PasskeySummary>>("api/account/passkeys", "The server returned no passkey list.");

	public Task<ApiResult<PasskeyChallenge>> PasskeyOptionsAsync(string currentPassword) =>
		Client.PostApiAsync<PasskeyOptionsRequest, PasskeyChallenge>(
			"api/account/passkeys/options", new PasskeyOptionsRequest(currentPassword), "The server started no passkey.");

	public Task<ApiResult<PasskeySummary>> AddPasskeyAsync(string ceremonyId, string? name, JsonElement credential) =>
		Client.PostApiAsync<AddPasskeyRequest, PasskeySummary>(
			"api/account/passkeys", new AddPasskeyRequest(ceremonyId, name, credential), "The passkey was added but the server described nothing.");

	public Task<ApiResult<Success>> RenamePasskeyAsync(string id, string name) =>
		Client.PutApiAsync($"api/account/passkeys/{Uri.EscapeDataString(id)}", new RenamePasskeyRequest(name));

	public Task<ApiResult<Success>> RemovePasskeyAsync(string id) =>
		Client.DeleteApiAsync($"api/account/passkeys/{Uri.EscapeDataString(id)}");

	public Task<ApiResult<Success>> LogoutAsync() => Client.PostApiAsync("api/account/logout");
}
