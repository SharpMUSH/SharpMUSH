using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the guest-character admin API.</summary>
/// <remarks>
/// It does not build its own <c>Authorization</c> header. The <c>"api"</c> client's
/// <see cref="AccountSessionBearerHandler"/> attaches the account-session bearer and hydrates the
/// session from <c>sessionStorage</c> first; a caller that sets the header itself suppresses that
/// hydration and sends a bare <c>Bearer</c> with no value during a page refresh.
/// </remarks>
public class AdminGuestsService(IHttpClientFactory httpClientFactory)
{
	public record GuestRow(int DbrefNumber, long CreationTime, string Name, bool InUse);

	public record GuestListResponse(
		bool GuestLoginsEnabled,
		int MaxGuests,
		string NextFreeName,
		IReadOnlyList<GuestRow> Guests);

	private record CreateGuestRequest(string? Name);

	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<GuestListResponse>> ListAsync() =>
		Client.GetApiAsync<GuestListResponse>("api/admin/guests", "The server returned no guest list.");

	public Task<ApiResult<GuestRow>> CreateAsync(string? name) =>
		Client.PostApiAsync<CreateGuestRequest, GuestRow>(
			"api/admin/guests", new CreateGuestRequest(name),
			"The guest was created but the server described nothing.");

	/// <param name="creationTime">
	/// From the row the operator clicked. A dbref number on its own stops identifying a character the
	/// moment one is nuked and another created, which is exactly what this panel does, so the number
	/// is sent with the stamp that pins it to one guest.
	/// </param>
	public Task<ApiResult<Success>> DeleteAsync(int dbrefNumber, long creationTime) =>
		Client.DeleteApiAsync($"api/admin/guests/{dbrefNumber}?created={creationTime}");
}
