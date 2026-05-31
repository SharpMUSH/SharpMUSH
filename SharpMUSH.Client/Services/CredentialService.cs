using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Stores MUSH player credentials in browser localStorage so the terminal can
/// auto-connect without requiring the user to type "connect name password" every time.
/// Passwords are stored as plain text in localStorage — suitable for a local/trusted
/// deployment but not recommended for public-facing production sites.
/// </summary>
public class CredentialService(IJSRuntime js)
{
	private const string PlayerKey = "sharpmush.player";
	private const string PasswordKey = "sharpmush.password";
	private const string ServerUriKey = "sharpmush.serverUri";

	public async Task<(string? PlayerName, string? Password, string? ServerUri)> LoadAsync()
	{
		var player = await js.InvokeAsync<string?>("localStorage.getItem", PlayerKey);
		var password = await js.InvokeAsync<string?>("localStorage.getItem", PasswordKey);
		var serverUri = await js.InvokeAsync<string?>("localStorage.getItem", ServerUriKey);
		return (player, password, serverUri);
	}

	public async Task SaveAsync(string? playerName, string? password, string? serverUri)
	{
		if (!string.IsNullOrWhiteSpace(playerName))
			await js.InvokeVoidAsync("localStorage.setItem", PlayerKey, playerName);
		else
			await js.InvokeVoidAsync("localStorage.removeItem", PlayerKey);

		if (!string.IsNullOrWhiteSpace(password))
			await js.InvokeVoidAsync("localStorage.setItem", PasswordKey, password);
		else
			await js.InvokeVoidAsync("localStorage.removeItem", PasswordKey);

		if (!string.IsNullOrWhiteSpace(serverUri))
			await js.InvokeVoidAsync("localStorage.setItem", ServerUriKey, serverUri);
		else
			await js.InvokeVoidAsync("localStorage.removeItem", ServerUriKey);
	}

	public async Task ClearAsync()
	{
		await js.InvokeVoidAsync("localStorage.removeItem", PlayerKey);
		await js.InvokeVoidAsync("localStorage.removeItem", PasswordKey);
		await js.InvokeVoidAsync("localStorage.removeItem", ServerUriKey);
	}
}
