using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>The command palette's server side: <c>GET api/palette</c> and <c>PUT api/palette/pins</c>.</summary>
public class PaletteClient(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>
	/// The entries, built-ins, pins and context stack of the session's character, or a failure (a visitor
	/// with no character, or a server that cannot answer). <paramref name="inputSession"/> is the
	/// <c>@input</c> session the Play terminal shows a prompt for.
	/// </summary>
	public virtual Task<ApiResult<PaletteResponse>> GetAsync(string? inputSession = null) =>
		Client.GetApiAsync<PaletteResponse>(
			Guid.TryParse(inputSession, out var session) ? $"api/palette?input={session}" : "api/palette",
			"The server returned no palette.");

	/// <summary>Writes the character's pins, in order.</summary>
	public virtual Task<ApiResult<Success>> SetPinsAsync(IReadOnlyList<string> pins) =>
		Client.PutApiAsync("api/palette/pins", new PalettePinsRequest(pins));
}
