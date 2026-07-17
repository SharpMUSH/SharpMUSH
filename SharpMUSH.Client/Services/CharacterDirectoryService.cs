using System.Net.Http.Json;
using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side character directory. Reads the full character list from the in-game
/// http_handler's routed softcode (<c>GET /http/characters</c> → <c>GET`CHARACTERS</c> on #4 —
/// see help sharphttp). Each row carries the character's objid, which is how profiles are
/// addressed (the profile view is a schema-driven <see cref="SchemaAppService"/> fetch).
/// </summary>
public class CharacterDirectoryService(IHttpClientFactory httpClientFactory, ILogger<CharacterDirectoryService> logger)
{
	/// <summary>
	/// A directory row from the GET`CHARACTERS softcode: name, objid, creation unix-ms, and the
	/// game-defined category (FN`CHARCAT). The portal imposes no categories of its own — blank
	/// (or absent, on handlers that predate categorization) means uncategorized.
	/// </summary>
	public record CharacterSummary(string Name, string Objid, long Created, string Category = "")
	{
		public DateTimeOffset CreatedAt => DateTimeOffset.FromUnixTimeMilliseconds(Created);

		/// <summary>The display dbref — the objid without its creation-time suffix (e.g. "#42").</summary>
		public string Dbref => Objid.Split(':')[0];
	}

	/// <summary>Returns every character, name-sorted; an empty list on failure.</summary>
	public async Task<IReadOnlyList<CharacterSummary>> ListAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var rows = await http.GetFromJsonAsync<List<CharacterSummary>>("http/characters");
			return rows is null ? [] : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}
		// These handlers are redefinable per game, so a malformed response is a configuration
		// mistake rather than a bug here: degrade to an empty list instead of taking the page down.
		// JsonException covers a body that is not JSON; InvalidOperationException covers a
		// Content-Type whose charset is unrecognised, which fails while reading the body, before
		// any parsing. NotSupportedException is documented on ReadFromJsonAsync for an unusable
		// content type — it does not fire on this stack today, but it is part of the API's contract.
		catch (Exception ex) when (ex is HttpRequestException or JsonException
			or InvalidOperationException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to load character directory.");
			return [];
		}
	}

	/// <summary>
	/// Returns the characters currently connected, name-sorted; an empty list on failure.
	/// Backed by <c>GET /http/online</c> → <c>GET`ONLINE</c> on #8, which reads lwho() — the same
	/// connection registry WHO reads. Distinct from <see cref="ListAsync"/>, which is the roster of
	/// every character that exists: a character being listed there implies nothing about presence.
	/// </summary>
	public async Task<IReadOnlyList<CharacterSummary>> ListOnlineAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var rows = await http.GetFromJsonAsync<List<CharacterSummary>>("http/online");
			return rows is null ? [] : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}
		// These handlers are redefinable per game, so a malformed response is a configuration
		// mistake rather than a bug here: degrade to an empty list instead of taking the page down.
		// JsonException covers a body that is not JSON; InvalidOperationException covers a
		// Content-Type whose charset is unrecognised, which fails while reading the body, before
		// any parsing. NotSupportedException is documented on ReadFromJsonAsync for an unusable
		// content type — it does not fire on this stack today, but it is part of the API's contract.
		catch (Exception ex) when (ex is HttpRequestException or JsonException
			or InvalidOperationException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to load the online character list.");
			return [];
		}
	}

	/// <summary>Resolves a character's objid by (case-insensitive) name via the directory; null if absent.</summary>
	public async Task<string?> ResolveObjidAsync(string name)
	{
		var rows = await ListAsync();
		return rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Objid;
	}
}
