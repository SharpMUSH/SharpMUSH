using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Stores the MUSH player name and server URI in browser localStorage.
/// Passwords are no longer stored locally — on connect the client fetches a
/// short-lived One-Time Token from the API, so the password is only ever sent
/// over HTTPS (not WebSocket, not localStorage).
/// </summary>
public class CredentialService(IJSRuntime js)
{
	private const string PlayerKey = "sharpmush.player";
	private const string ServerUriKey = "sharpmush.serverUri";

	// Legacy key — read once to migrate existing users, then clear.
	private const string LegacyPasswordKey = "sharpmush.password";

	public async Task<(string? PlayerName, string? Password, string? ServerUri)> LoadAsync()
	{
		var player = await js.InvokeAsync<string?>("localStorage.getItem", PlayerKey);
		var serverUri = await js.InvokeAsync<string?>("localStorage.getItem", ServerUriKey);

		// Migrate: read legacy stored password once, then erase it from storage.
		var password = await js.InvokeAsync<string?>("localStorage.getItem", LegacyPasswordKey);
		if (password is not null)
			await js.InvokeVoidAsync("localStorage.removeItem", LegacyPasswordKey);

		return (player, password, serverUri);
	}

	/// <summary>
	/// Save credentials.  <paramref name="password"/> is only used on the first call
	/// (to migrate existing users) and is never persisted to localStorage.
	/// </summary>
	public async Task SaveAsync(string? playerName, string? password, string? serverUri)
	{
		if (!string.IsNullOrWhiteSpace(playerName))
			await js.InvokeVoidAsync("localStorage.setItem", PlayerKey, playerName);
		else
			await js.InvokeVoidAsync("localStorage.removeItem", PlayerKey);

		// Passwords are no longer stored — clear any legacy value
		await js.InvokeVoidAsync("localStorage.removeItem", LegacyPasswordKey);

		if (!string.IsNullOrWhiteSpace(serverUri))
			await js.InvokeVoidAsync("localStorage.setItem", ServerUriKey, serverUri);
		else
			await js.InvokeVoidAsync("localStorage.removeItem", ServerUriKey);
	}

	public async Task ClearAsync()
	{
		await js.InvokeVoidAsync("localStorage.removeItem", PlayerKey);
		await js.InvokeVoidAsync("localStorage.removeItem", LegacyPasswordKey);
		await js.InvokeVoidAsync("localStorage.removeItem", ServerUriKey);
	}
}
