using SharpMUSH.Library.DiscriminatedUnions;
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
	private const string CharactersRoute = "http/characters";
	private const string OnlineRoute = "http/online";

	// Each read is a softcode iteration over every player, draws on the softcode HTTP rate limit, and one
	// page has several readers (the page, the sidebar, the aside widgets, the wiki's mentions, the home
	// page's stats tile). They share one in-flight read and a short memo; a failed read is not remembered,
	// so the next caller asks again.
	private readonly ShortMemo<ServerResult<IReadOnlyList<CharacterSummary>>> _roster =
		new(TimeSpan.FromSeconds(30), r => r.Value is IReadOnlyList<CharacterSummary>);

	private readonly ShortMemo<ServerResult<IReadOnlyList<CharacterSummary>>> _online =
		new(TimeSpan.FromSeconds(10), r => r.Value is IReadOnlyList<CharacterSummary>);

	/// <summary>
	/// A directory row from the GET`CHARACTERS softcode: name, objid, creation unix-ms, and the
	/// game-defined category (FN`CHARCAT). The portal imposes no categories of its own — blank
	/// (or absent, on handlers that predate categorization) means uncategorized. Image is the
	/// character's IMAGE attribute (profile-handler 1.5); blank or absent means none.
	/// </summary>
	public record CharacterSummary(string Name, string Objid, long Created, string Category = "", string? Image = null)
	{
		public DateTimeOffset CreatedAt => DateTimeOffset.FromUnixTimeMilliseconds(Created);

		/// <summary>The display dbref — the objid without its creation-time suffix (e.g. "#42").</summary>
		public string Dbref => Objid.Split(':')[0];
	}

	/// <summary>
	/// Returns every character, name-sorted; <see cref="Error"/> if the request failed.
	/// </summary>
	/// <remarks>
	/// The failed arm is not decoration: "nobody" and "we could not ask" are different facts. A
	/// failed fetch that degraded to an empty list would have the dashboard's "Characters" tile print
	/// a confident <c>0</c> for a game with characters in it. A count is an assertion about the game,
	/// and a caller that never got an answer must not make one — so the failure is in the type, where
	/// a consumer has to decide what to do with it.
	/// </remarks>
	public Task<ServerResult<IReadOnlyList<CharacterSummary>>> ListAsync(CancellationToken cancellationToken = default) =>
		Shared(_roster, CharactersRoute, "Failed to load character directory.", cancellationToken);

	/// <summary>
	/// Returns the characters currently connected, name-sorted and one row per character;
	/// <see cref="Error"/> if the request failed. Backed by <c>GET /http/online</c> →
	/// <c>GET`ONLINE</c> on #8, which reads mwho(). Distinct from <see cref="ListAsync"/>, which is
	/// the roster of every character that exists: a character being listed there implies nothing
	/// about presence.
	/// </summary>
	public Task<ServerResult<IReadOnlyList<CharacterSummary>>> ListOnlineAsync(CancellationToken cancellationToken = default) =>
		Shared(_online, OnlineRoute, "Failed to load the online character list.", cancellationToken);

	/// <summary>
	/// A caller that has already given up is told so rather than handed a shared answer; one that gives up
	/// while waiting ends only its own wait.
	/// </summary>
	private Task<ServerResult<IReadOnlyList<CharacterSummary>>> Shared(ShortMemo<ServerResult<IReadOnlyList<CharacterSummary>>> memo,
		string route, string failureMessage, CancellationToken cancellationToken) =>
		cancellationToken.IsCancellationRequested
			? Task.FromCanceled<ServerResult<IReadOnlyList<CharacterSummary>>>(cancellationToken)
			: memo.GetAsync(() => FetchAsync(route, failureMessage)).WaitAsync(cancellationToken);

	/// <summary>
	/// The one request behind both reads. It runs without any caller's token because concurrent callers
	/// share it: a caller's cancellation ends only that caller's wait (<see cref="Task.WaitAsync(CancellationToken)"/>),
	/// and surfaces to it as an <see cref="OperationCanceledException"/>.
	/// </summary>
	private async Task<ServerResult<IReadOnlyList<CharacterSummary>>> FetchAsync(string route, string failureMessage)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var rows = await http.GetFromJsonAsync<List<CharacterSummary>>(route);
			return new ServerResult<IReadOnlyList<CharacterSummary>>(Normalize(rows));
		}
		catch (Exception ex) when (IsRequestFailure(ex))
		{
			logger.LogWarning(ex, "{FailureMessage}", failureMessage);
			return new Error();
		}
	}

	/// <summary>
	/// True for a failure that belongs in the <see cref="Error"/> arm rather than up the stack.
	/// </summary>
	/// <remarks>
	/// These handlers are redefinable per game, so a malformed response is a configuration mistake
	/// rather than a bug here: degrade instead of taking the page down. JsonException covers a body
	/// that is not JSON; InvalidOperationException covers a Content-Type whose charset is
	/// unrecognised, which fails while reading the body, before any parsing. NotSupportedException
	/// is documented on ReadFromJsonAsync for an unusable content type — it does not fire on this
	/// stack today, but it is part of the API's contract.
	/// <para>
	/// A request that exceeds <see cref="HttpClient.Timeout"/> surfaces as a
	/// <see cref="TaskCanceledException"/> (an <see cref="OperationCanceledException"/>, which is
	/// not an <see cref="InvalidOperationException"/>), so it used to escape as an unhandled
	/// exception out of a component's OnInitializedAsync — a slow game taking the page down, which
	/// is the failure mode this service exists to avoid. A cancellation the caller actually asked
	/// for is a different fact: it means the caller stopped wanting an answer, not that the game
	/// could not give one, and rendering "unavailable" for a navigation the user themselves
	/// abandoned would be a lie in the other direction. That one propagates — and it never reaches
	/// here: the shared request carries no caller's token, so every cancellation inside it is the
	/// timeout, and a caller's own cancellation ends only that caller's wait.
	/// </para>
	/// </remarks>
	private static bool IsRequestFailure(Exception ex) =>
		ex is OperationCanceledException or HttpRequestException or JsonException
			or InvalidOperationException or NotSupportedException;

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
	/// Resolves a character's objid by (case-insensitive) name via the directory.
	/// <see cref="NotFound"/> means no character answers to that name; <see cref="Error"/> means the
	/// directory could not be read.
	/// </summary>
	/// <remarks>
	/// The same two facts <see cref="ListAsync"/> keeps apart, kept apart here as well. A caller may
	/// decline to fetch either way, but that is a fact about the caller, not about the answer: "there
	/// is no such character" is a 404 page and "we could not ask the game" is not, and a caller that
	/// wants to say so should not have to widen this signature first.
	/// </remarks>
	public async Task<ObjidResolution> ResolveObjidAsync(string name, CancellationToken cancellationToken = default)
	{
		return await ListAsync(cancellationToken) switch
		{
			IReadOnlyList<CharacterSummary> rows =>
				rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) is { } match
					? match.Objid
					: new NotFound(),
			Error error => error
		};
	}
}
