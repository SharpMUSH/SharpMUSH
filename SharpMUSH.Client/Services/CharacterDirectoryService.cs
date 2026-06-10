using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side character directory. Reads the full character list from the server
/// (<c>GET /api/characters</c>) for the public directory at <c>/characters</c>.
/// </summary>
public class CharacterDirectoryService(IHttpClientFactory httpClientFactory, ILogger<CharacterDirectoryService> logger)
{
	/// <summary>A directory row mirroring <c>CharactersController.CharacterSummaryDto</c>.</summary>
	public record CharacterSummary(int Dbref, string Name, string Slug, DateTimeOffset CreatedAt);

	/// <summary>Returns every character, name-sorted; an empty list on failure.</summary>
	public async Task<IReadOnlyList<CharacterSummary>> ListAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var rows = await http.GetFromJsonAsync<List<CharacterSummary>>("api/characters");
			return rows ?? [];
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Failed to load character directory.");
			return [];
		}
	}
}
