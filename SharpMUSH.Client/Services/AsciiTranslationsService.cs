using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the ASCII translations table: what a client without Unicode is sent for a character.</summary>
public class AsciiTranslationsService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<Dictionary<string, string>>> GetAsync() =>
		Client.GetApiAsync<Dictionary<string, string>>("api/asciitranslations", "The server returned no ASCII translations.");

	public Task<ApiResult<Success>> SetAsync(string character, string text) =>
		Client.PostApiAsync("api/asciitranslations", new AsciiTranslationRequest(character, text));

	public Task<ApiResult<Success>> DeleteAsync(string character) =>
		Client.DeleteApiAsync($"api/asciitranslations/{Uri.EscapeDataString(character)}");
}
