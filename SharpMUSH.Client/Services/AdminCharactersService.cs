using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the admin characters API (<c>api/admin/characters</c>).</summary>
public class AdminCharactersService(IHttpClientFactory httpClientFactory)
{
	/// <summary>The list's filters; a null or empty one matches every character.</summary>
	public sealed record Filter(string? Search = null, bool? Online = null, string? Flag = null, string? Account = null);

	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<AdminCharacterPage>> ListAsync(Filter filter, int page, int pageSize) =>
		Client.GetApiAsync<AdminCharacterPage>(
			ApiQuery.Build("api/admin/characters",
				("search", filter.Search),
				("online", filter.Online is { } online ? (online ? "true" : "false") : null),
				("flag", filter.Flag),
				("account", filter.Account),
				("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture)),
				("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture))),
			"The server returned no character list.");

	public Task<ApiResult<AdminCharacterDetail>> GetAsync(int dbrefNumber) =>
		Client.GetApiAsync<AdminCharacterDetail>($"api/admin/characters/{dbrefNumber}",
			"The server returned no character.");

	/// <param name="created">The creation time the page was showing, so a recycled number is refused.</param>
	/// <param name="reason">Why, when the staff member gave a reason; kept in the audit log.</param>
	public Task<ApiResult<Success>> BootAsync(int dbrefNumber, long created, string? reason = null) =>
		Client.PostApiAsync(ApiQuery.Build($"api/admin/characters/{dbrefNumber}/boot",
			("created", created.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("reason", reason)));

	/// <summary>Fires the game's <c>PLAYER`WARN</c> event for the character, with the reason.</summary>
	public Task<ApiResult<Success>> WarnAsync(int dbrefNumber, long created, string reason) =>
		Client.PostApiAsync($"api/admin/characters/{dbrefNumber}/warn?created={created}", new AdminWarnRequest(reason));
}
