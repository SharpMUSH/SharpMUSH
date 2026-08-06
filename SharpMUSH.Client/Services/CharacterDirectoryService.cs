using OneOf;
using OneOf.Types;
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

	/// <summary>
	/// Returns every character, name-sorted; <see cref="Error"/> if the request failed.
	/// </summary>
	/// <remarks>
	/// The failed arm is not decoration: "nobody" and "we could not ask" are different facts that
	/// used to render identically, because a failed fetch degraded to an empty list. The
	/// dashboard's "Characters" tile then printed a confident <c>0</c> for a game with four
	/// characters in it. A count is an assertion about the game, and a caller that never got an
	/// answer must not make one — so the failure is in the type, where a consumer has to decide
	/// what to do with it rather than inherit the old lie by accident.
	/// </remarks>
	public async Task<OneOf<IReadOnlyList<CharacterSummary>, Error>> ListAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var rows = await http.GetFromJsonAsync<List<CharacterSummary>>("http/characters");
			return OneOf<IReadOnlyList<CharacterSummary>, Error>.FromT0(Normalize(rows));
		}
		// These handlers are redefinable per game, so a malformed response is a configuration
		// mistake rather than a bug here: degrade to the failed arm instead of taking the page down.
		// JsonException covers a body that is not JSON; InvalidOperationException covers a
		// Content-Type whose charset is unrecognised, which fails while reading the body, before
		// any parsing. NotSupportedException is documented on ReadFromJsonAsync for an unusable
		// content type — it does not fire on this stack today, but it is part of the API's contract.
		catch (Exception ex) when (ex is HttpRequestException or JsonException
			or InvalidOperationException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to load character directory.");
			return new Error();
		}
	}

	/// <summary>
	/// Returns the characters currently connected, name-sorted and one row per character;
	/// <see cref="Error"/> if the request failed. Backed by <c>GET /http/online</c> →
	/// <c>GET`ONLINE</c> on #8, which reads mwho(). Distinct from <see cref="ListAsync"/>, which is
	/// the roster of every character that exists: a character being listed there implies nothing
	/// about presence.
	/// </summary>
	public async Task<OneOf<IReadOnlyList<CharacterSummary>, Error>> ListOnlineAsync()
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var rows = await http.GetFromJsonAsync<List<CharacterSummary>>("http/online");
			return OneOf<IReadOnlyList<CharacterSummary>, Error>.FromT0(Normalize(rows));
		}
		// These handlers are redefinable per game, so a malformed response is a configuration
		// mistake rather than a bug here: degrade to the failed arm instead of taking the page down.
		// JsonException covers a body that is not JSON; InvalidOperationException covers a
		// Content-Type whose charset is unrecognised, which fails while reading the body, before
		// any parsing. NotSupportedException is documented on ReadFromJsonAsync for an unusable
		// content type — it does not fire on this stack today, but it is part of the API's contract.
		catch (Exception ex) when (ex is HttpRequestException or JsonException
			or InvalidOperationException or NotSupportedException)
		{
			logger.LogWarning(ex, "Failed to load the online character list.");
			return new Error();
		}
	}

	/// <summary>
	/// One row per character, name-sorted. A <c>null</c> body (a bare <c>null</c> literal, which is
	/// valid JSON) reads as an empty list, not a failure.
	/// </summary>
	/// <remarks>
	/// Deduplication on objid belongs here rather than in each widget so that "players online"
	/// means people, not sockets, for every consumer present and future. It matters most for
	/// GET`ONLINE: mwho() now lists each player once, but the route is redefinable per game and a
	/// redefinition over ports()/lports() is a connection list, which is exactly how the portal
	/// came to report "2, 0, 1, 14" players online for a world with four characters and to render
	/// "Castor, Solitaire, Solitaire, Solitaire". Identity is the objid, never the name: two
	/// characters may share a display name, and the same character may not share an objid. The
	/// multiplicity is dropped rather than surfaced — the portal has nothing to say about a
	/// character being connected twice, and a deduplicated upstream can no longer report it
	/// truthfully anyway; WHO and ports() remain the per-connection view.
	/// </remarks>
	private static IReadOnlyList<CharacterSummary> Normalize(List<CharacterSummary>? rows) =>
		rows is null
			? []
			: rows
				.DistinctBy(r => r.Objid, StringComparer.Ordinal)
				.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

	/// <summary>
	/// Resolves a character's objid by (case-insensitive) name via the directory; null if absent.
	/// A failed directory read is also null here: the caller (a route template with an
	/// <c>{objid}</c> hole) can do nothing with either answer but decline to fetch.
	/// </summary>
	public async Task<string?> ResolveObjidAsync(string name)
	{
		var rows = await ListAsync();
		return rows.Match(
			found => found.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Objid,
			_ => null);
	}
}
