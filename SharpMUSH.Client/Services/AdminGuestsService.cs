using System.Net.Http.Json;
using OneOf;
using OneOf.Types;

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
	// in the browser, and its whole contract is to turn "the call did not work" into something the admin
	// panel can render next to the guest list. An exception type nobody enumerated — a malformed base
	// address, a handler an analyzer has not heard of — would otherwise escape into the render loop and
	// take the page down, which is a worse outcome than showing the message. The catch is the boundary,
	// not a swallow: the detail reaches the operator either way.
	//
	// The results are OneOf rather than a (value, error) tuple of nullables. A tuple has a fourth state
	// nothing means — (null, null) — and it was reachable: a body that deserialises to null is a
	// successful response with no value, which left the panel showing an empty roster and no
	// explanation. There is no such arm here; every path names either a list or a reason.
	public async Task<OneOf<GuestListResponse, ApiFailure>> ListAsync()
	{
		try
		{
			var response = await CreateClient().GetAsync("api/admin/guests");
			if (!response.IsSuccessStatusCode)
				return ApiFailure.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync());

			return await response.Content.ReadFromJsonAsync<GuestListResponse>()
				?? (OneOf<GuestListResponse, ApiFailure>)new ApiFailure(
					ApiFailureKind.Unexpected, "The server returned no guest list.", response.StatusCode);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	public async Task<OneOf<GuestRow, ApiFailure>> CreateAsync(string? name)
	{
		try
		{
			var response = await CreateClient().PostAsJsonAsync("api/admin/guests", new CreateGuestRequest(name));
			if (!response.IsSuccessStatusCode)
				return ApiFailure.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync());

			return await response.Content.ReadFromJsonAsync<GuestRow>()
				?? (OneOf<GuestRow, ApiFailure>)new ApiFailure(
					ApiFailureKind.Unexpected, "The guest was created but the server described nothing.",
					response.StatusCode);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	/// <param name="creationTime">
	/// From the row the operator clicked. A dbref number on its own stops identifying a character the
	/// moment one is nuked and another created, which is exactly what this panel does, so the number
	/// is sent with the stamp that pins it to one guest.
	/// </param>
	public async Task<OneOf<Success, ApiFailure>> DeleteAsync(int dbrefNumber, long creationTime)
	{
		try
		{
			var response = await CreateClient()
				.DeleteAsync($"api/admin/guests/{dbrefNumber}?created={creationTime}");
			return response.IsSuccessStatusCode
				? new Success()
				: ApiFailure.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync());
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}
}
