using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the guest-character admin API (account-session bearer).</summary>
public class AdminGuestsService(IHttpClientFactory httpClientFactory, AccountAuthService accountAuth)
{
	public record GuestRow(int DbrefNumber, long CreationTime, string Name, bool InUse);

	public record GuestListResponse(
		bool GuestLoginsEnabled,
		int MaxGuests,
		string NextFreeName,
		IReadOnlyList<GuestRow> Guests);

	private record CreateGuestRequest(string? Name);

	private HttpClient CreateClient()
	{
		var http = httpClientFactory.CreateClient("api");
		http.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accountAuth.AccountSessionToken);
		return http;
	}

	// Every call here catches Exception on purpose, and narrowing it would be a regression. This runs
	// in the browser, and its whole contract is to turn "the call did not work" into a string the admin
	// panel can render next to the guest list. An exception type nobody enumerated — a malformed base
	// address, a handler an analyzer has not heard of — would otherwise escape into the render loop and
	// take the page down, which is a worse outcome than showing the message. The catch is the boundary,
	// not a swallow: the text reaches the operator either way.
	public async Task<(GuestListResponse? List, string? Error)> ListAsync()
	{
		try
		{
			var response = await CreateClient().GetAsync("api/admin/guests");
			if (!response.IsSuccessStatusCode)
				return (null, await response.Content.ReadAsStringAsync());
			return (await response.Content.ReadFromJsonAsync<GuestListResponse>(), null);
		}
		catch (Exception ex)
		{
			return (null, ex.Message);
		}
	}

	public async Task<(GuestRow? Created, string? Error)> CreateAsync(string? name)
	{
		try
		{
			var response = await CreateClient().PostAsJsonAsync("api/admin/guests", new CreateGuestRequest(name));
			if (!response.IsSuccessStatusCode)
				return (null, await response.Content.ReadAsStringAsync());
			return (await response.Content.ReadFromJsonAsync<GuestRow>(), null);
		}
		catch (Exception ex)
		{
			return (null, ex.Message);
		}
	}

	public async Task<(bool Success, string? Error)> DeleteAsync(int dbrefNumber)
	{
		try
		{
			var response = await CreateClient().DeleteAsync($"api/admin/guests/{dbrefNumber}");
			return response.IsSuccessStatusCode
				? (true, null)
				: (false, await response.Content.ReadAsStringAsync());
		}
		catch (Exception ex)
		{
			return (false, ex.Message);
		}
	}
}
