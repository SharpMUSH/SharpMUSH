using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Stores the MUSH player name in browser localStorage.
/// Passwords are no longer stored locally — on connect the client fetches a
/// short-lived One-Time Token from the API, so the password is only ever sent
/// over HTTPS (not WebSocket, not localStorage).
/// The server URI is no longer stored either: the terminal derives it from the
/// portal's own origin (see GlobalTerminal), so a stale saved address cannot
/// point the connection at the wrong host.
/// </summary>
public class CredentialService(IJSRuntime js)
{
	private const string PlayerKey = "sharpmush.player";

	// Legacy keys — read/cleared once to migrate existing users.
	private const string LegacyPasswordKey = "sharpmush.password";
	private const string LegacyServerUriKey = "sharpmush.serverUri";

	public async Task<(string? PlayerName, string? Password)> LoadAsync()
	{
		var player = await js.InvokeAsync<string?>("localStorage.getItem", PlayerKey);

		// Migrate: read legacy stored password once, then erase it from storage.
		var password = await js.InvokeAsync<string?>("localStorage.getItem", LegacyPasswordKey);
		if (password is not null)
			await js.InvokeVoidAsync("localStorage.removeItem", LegacyPasswordKey);

		// Legacy stored server URIs (often ws://localhost:4202/ws) are simply dropped.
		await js.InvokeVoidAsync("localStorage.removeItem", LegacyServerUriKey);

		return (player, password);
	}

	/// <summary>
	/// Save credentials.  <paramref name="password"/> is only used on the first call
	/// (to migrate existing users) and is never persisted to localStorage.
	/// </summary>
	public async Task SaveAsync(string? playerName, string? password)
	{
		if (!string.IsNullOrWhiteSpace(playerName))
			await js.InvokeVoidAsync("localStorage.setItem", PlayerKey, playerName);
		else
			await js.InvokeVoidAsync("localStorage.removeItem", PlayerKey);

		// Passwords are no longer stored — clear any legacy value
		await js.InvokeVoidAsync("localStorage.removeItem", LegacyPasswordKey);
	}

	public async Task ClearAsync()
	{
		await js.InvokeVoidAsync("localStorage.removeItem", PlayerKey);
		await js.InvokeVoidAsync("localStorage.removeItem", LegacyPasswordKey);
		await js.InvokeVoidAsync("localStorage.removeItem", LegacyServerUriKey);
	}
}
