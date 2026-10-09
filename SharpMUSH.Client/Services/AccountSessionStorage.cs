using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// What this tab keeps of its account session in <c>sessionStorage</c>, and the order it is written in.
/// </summary>
/// <remarks>
/// <para>Split out of <see cref="AccountAuthService"/>, which used to read and write these keys
/// inline among its HTTP calls. Storage is tab-scoped: closing the tab ends the session it held, and
/// two tabs on one account each keep their own token (and so their own acting character).</para>
///
/// <para>The token is written with the character it is bound to, so a tab whose session ran out
/// while it was closed or idle (a deploy, a long lunch) asks the remembered login for that character
/// again rather than the account's primary one.</para>
///
/// <para>Only the token, its character, the name and the must-change-password flag are ever read back. The role and
/// grants are written for the benefit of nothing but the next sign-in's overwrite — they are never
/// authority, because roles can migrate or be revoked while a tab is closed; the session read restores
/// them from the server instead.</para>
/// </remarks>
public sealed class AccountSessionStorage(IJSRuntime js)
{
	private const string SessionTokenKey = "sharpmush.account.sessionToken";
	private const string UsernameKey = "sharpmush.account.username";
	private const string MustChangePasswordKey = "sharpmush.account.mustChangePassword";
	private const string RoleKey = "sharpmush.account.role";
	private const string PermissionsKey = "sharpmush.account.permissions";
	private const string LoggedOutKey = "sharpmush.account.loggedOut";
	private const string CharacterKey = "sharpmush.account.character";

	/// <summary>The session a reload brings back.</summary>
	/// <param name="Character">The character <paramref name="Token"/> was bound to, when it was bound to one.</param>
	public sealed record StoredSession(string Token, string? Username, bool MustChangePassword, BoundCharacter? Character);

	/// <summary>A character a session token is bound to: its dbref and creation time.</summary>
	public readonly record struct BoundCharacter(int DbrefNumber, long CreationTime)
	{
		public static BoundCharacter? Of(AccountAuthService.CharacterSummary? character) =>
			character is null ? null : new BoundCharacter(character.DbrefNumber, character.CreationTime);

		public override string ToString() => $"{DbrefNumber}:{CreationTime}";

		public static BoundCharacter? Parse(string? stored) =>
			stored?.Split(':') is [var key, var created]
			&& int.TryParse(key, out var dbref) && long.TryParse(created, out var creationTime)
				? new BoundCharacter(dbref, creationTime)
				: null;
	}

	/// <summary>Whether this tab was explicitly signed out, and has not signed in since.</summary>
	public async Task<bool> IsLoggedOutAsync() =>
		IsTrue(await js.GetItemAsync(BrowserStore.Session, LoggedOutKey));

	/// <summary>The session this tab holds, or <see cref="NotFound"/> when it holds none.</summary>
	public async Task<Found<StoredSession>> ReadAsync()
	{
		if (await js.GetItemAsync(BrowserStore.Session, SessionTokenKey) is not { } token)
			return new NotFound();

		return new StoredSession(
			token,
			await js.GetItemAsync(BrowserStore.Session, UsernameKey),
			IsTrue(await js.GetItemAsync(BrowserStore.Session, MustChangePasswordKey)),
			BoundCharacter.Parse(await js.GetItemAsync(BrowserStore.Session, CharacterKey)));
	}

	/// <summary>
	/// Writes a freshly minted session and clears the signed-out latch. Token first: hydration ignores
	/// every other key without one, so a write that fails part-way is abandoned by dropping it.
	/// </summary>
	/// <returns>False, with nothing kept, when this tab could not store the session.</returns>
	public async Task<bool> TryWriteAsync(string token, BoundCharacter? character,
		string username, bool mustChangePassword, string? role, IReadOnlyList<string> permissions)
	{
		var written = await TryWriteTokenAsync(token, character)
			&& await js.SetItemAsync(BrowserStore.Session, UsernameKey, username)
			&& await js.SetItemAsync(BrowserStore.Session, MustChangePasswordKey, mustChangePassword.ToString())
			&& (role is null
				? await js.RemoveItemAsync(BrowserStore.Session, RoleKey)
				: await js.SetItemAsync(BrowserStore.Session, RoleKey, role))
			&& await js.SetItemAsync(BrowserStore.Session, PermissionsKey, JsonSerializer.Serialize(permissions))
			// Any successful login/register/setup clears a prior explicit logout.
			&& await js.RemoveItemAsync(BrowserStore.Session, LoggedOutKey);

		if (!written)
			await js.RemoveItemAsync(BrowserStore.Session, SessionTokenKey);
		return written;
	}

	/// <summary>Replaces only the token and its character — a character switch keeps the account and everything else.</summary>
	/// <returns>False when this tab could not store the token. A character that could not be stored is only
	/// forgotten: a resume then binds the account's primary character, as it did before.</returns>
	public async Task<bool> TryWriteTokenAsync(string token, BoundCharacter? character)
	{
		if (!await js.SetItemAsync(BrowserStore.Session, SessionTokenKey, token))
			return false;
		if (character is not { } bound || !await js.SetItemAsync(BrowserStore.Session, CharacterKey, bound.ToString()))
			await js.RemoveItemAsync(BrowserStore.Session, CharacterKey);
		return true;
	}

	public async Task WriteUsernameAsync(string username) =>
		await js.SetItemAsync(BrowserStore.Session, UsernameKey, username);

	public async Task WriteMustChangePasswordAsync(bool value) =>
		await js.SetItemAsync(BrowserStore.Session, MustChangePasswordKey, value.ToString());

	/// <summary>
	/// Forgets the session and latches the explicit sign-out, which sticks until the next successful
	/// sign-in in this tab so that a silent re-authentication (dev-mode debug auth) cannot undo it.
	/// </summary>
	public async Task ClearAndLatchLoggedOutAsync()
	{
		await js.RemoveItemAsync(BrowserStore.Session, SessionTokenKey);
		await js.RemoveItemAsync(BrowserStore.Session, CharacterKey);
		await js.RemoveItemAsync(BrowserStore.Session, UsernameKey);
		await js.RemoveItemAsync(BrowserStore.Session, MustChangePasswordKey);
		await js.RemoveItemAsync(BrowserStore.Session, RoleKey);
		await js.RemoveItemAsync(BrowserStore.Session, PermissionsKey);
		await js.SetItemAsync(BrowserStore.Session, LoggedOutKey, bool.TrueString);
	}

	private static bool IsTrue(string? stored) =>
		string.Equals(stored, bool.TrueString, StringComparison.OrdinalIgnoreCase);
}
