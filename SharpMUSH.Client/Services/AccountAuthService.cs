using System.Net.Http.Json;
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
	ILogger<AccountAuthService> logger)
{
	private const string SessionTokenKey = "sharpmush.account.sessionToken";
	private const string DisplayNameKey = "sharpmush.account.displayName";

	// ── DTO records ─────────────────────────────────────────────────────────

	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);

	private record AccountLoginRequest(string Identifier, string Password);
	private record AccountRegisterRequest(string DisplayName, string? Email, string Password);
	private record AccountLoginResponse(string AccountId, string DisplayName, IReadOnlyList<CharacterSummary> Characters, string AccountSessionToken);
	private record MushTokenWithAccountRequest(string AccountSessionToken, int CharacterDbrefNumber, long CharacterCreationTime);
	private record MushTokenResponse(string Token, int ExpiresIn);
	private record CreateCharacterRequest(string Name, string Password);
	private record CreateCharacterResponse(int DbrefNumber, long? CreationTime);
	private record ChangePasswordRequest(string OldPassword, string NewPassword);
	private record ChangeEmailRequest(string? NewEmail, string CurrentPassword);
	private record ChangeDisplayNameRequest(string NewDisplayName);

	// ── In-memory state ──────────────────────────────────────────────────────

	public string? AccountSessionToken { get; private set; }
	public string? DisplayName { get; private set; }
	public IReadOnlyList<CharacterSummary> Characters { get; private set; } = [];
	public bool IsLoggedIn => AccountSessionToken is not null;

	// ── Initialization ────────────────────────────────────────────────────────

	public async Task InitAsync()
	{
		AccountSessionToken = await js.InvokeAsync<string?>("sessionStorage.getItem", SessionTokenKey);
		DisplayName = await js.InvokeAsync<string?>("localStorage.getItem", DisplayNameKey);
	}

	// ── Login / Register ─────────────────────────────────────────────────────

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

			await PersistSessionAsync(result.AccountSessionToken, result.DisplayName);
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
		string displayName, string? email, string password)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/auth/account-register",
				new AccountRegisterRequest(displayName, string.IsNullOrWhiteSpace(email) ? null : email, password));

			if (!response.IsSuccessStatusCode)
				return (false, await response.Content.ReadAsStringAsync(), []);

			var result = await response.Content.ReadFromJsonAsync<AccountLoginResponse>();
			if (result is null) return (false, "Unexpected server response.", []);

			await PersistSessionAsync(result.AccountSessionToken, result.DisplayName);
			Characters = result.Characters;
			return (true, null, result.Characters);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Account registration failed");
			return (false, ex.Message, []);
		}
	}

	// ── OTT for a character ───────────────────────────────────────────────────

	/// <summary>
	/// Exchange an account session token + character selection for a MUSH OTT.
	/// </summary>
	public async Task<string?> GetOttForCharacterAsync(CharacterSummary character)
	{
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

	// ── Character management ─────────────────────────────────────────────────

	public async Task<IReadOnlyList<CharacterSummary>> GetCharactersAsync()
	{
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

	// ── Profile management ────────────────────────────────────────────────────

	public async Task<(bool Success, string? Error)> ChangePasswordAsync(string oldPassword, string newPassword)
	{
		if (AccountSessionToken is null) return (false, "Not logged in.");
		return await PutAsync("api/account/password", new ChangePasswordRequest(oldPassword, newPassword));
	}

	public async Task<(bool Success, string? Error)> ChangeEmailAsync(string? newEmail, string currentPassword)
	{
		if (AccountSessionToken is null) return (false, "Not logged in.");
		return await PutAsync("api/account/email", new ChangeEmailRequest(newEmail, currentPassword));
	}

	public async Task<(bool Success, string? Error)> ChangeDisplayNameAsync(string newDisplayName)
	{
		if (AccountSessionToken is null) return (false, "Not logged in.");
		var (success, error) = await PutAsync("api/account/display-name", new ChangeDisplayNameRequest(newDisplayName));
		if (success)
		{
			DisplayName = newDisplayName;
			await js.InvokeVoidAsync("localStorage.setItem", DisplayNameKey, newDisplayName);
		}
		return (success, error);
	}

	// ── Logout ────────────────────────────────────────────────────────────────

	public async Task LogoutAsync()
	{
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
		DisplayName = null;
		Characters = [];
		await js.InvokeVoidAsync("sessionStorage.removeItem", SessionTokenKey);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private async Task PersistSessionAsync(string token, string displayName)
	{
		AccountSessionToken = token;
		DisplayName = displayName;
		await js.InvokeVoidAsync("sessionStorage.setItem", SessionTokenKey, token);
		await js.InvokeVoidAsync("localStorage.setItem", DisplayNameKey, displayName);
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
