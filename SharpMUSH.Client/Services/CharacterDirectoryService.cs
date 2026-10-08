using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side character directory. Reads the full character list from the in-game
/// http_handler's routed softcode (<c>GET /http/characters</c> → <c>GET`CHARACTERS</c> on #4 —
/// see help sharphttp). Each row carries the character's objid, which is how profiles are
/// addressed (the profile view is a schema-driven <see cref="SchemaAppService"/> fetch).
/// </summary>
public class CharacterDirectoryService(IHttpClientFactory httpClientFactory, ILogger<CharacterDirectoryService> logger)
	: ICharacterPictures
{
	private const string CharactersRoute = "http/characters";
	private const string OnlineRoute = "http/online";

	// Each read is a softcode iteration over every player, draws on the softcode HTTP rate limit, and one
	// page has several readers (the page, the sidebar, the aside widgets, the wiki's mentions, the home
	// page's stats tile). They share one in-flight read and a short memo; a failed read is not remembered,
	// so the next caller asks again.
	private readonly ShortMemo<ApiResult<IReadOnlyList<CharacterSummary>>> _roster =
		new(TimeSpan.FromSeconds(30), r => r is IReadOnlyList<CharacterSummary>);

	private readonly ShortMemo<ApiResult<IReadOnlyList<CharacterSummary>>> _online =
		new(TimeSpan.FromSeconds(10), r => r is IReadOnlyList<CharacterSummary>);

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
	/// Returns every character, name-sorted; the <see cref="ApiFailure"/> if the request failed.
	/// </summary>
	/// <remarks>
	/// The failed arm is not decoration: "nobody" and "we could not ask" are different facts. A
	/// failed fetch that degraded to an empty list would have the dashboard's "Characters" tile print
	/// a confident <c>0</c> for a game with characters in it. A count is an assertion about the game,
	/// and a caller that never got an answer must not make one — so the failure is in the type, where
	/// a consumer has to decide what to do with it.
	/// </remarks>
	public Task<ApiResult<IReadOnlyList<CharacterSummary>>> ListAsync(CancellationToken cancellationToken = default) =>
		Shared(_roster, CharactersRoute, "Failed to load character directory.", cancellationToken);

	/// <summary>
	/// Returns the characters currently connected, name-sorted and one row per character;
	/// the <see cref="ApiFailure"/> if the request failed. Backed by <c>GET /http/online</c> →
	/// <c>GET`ONLINE</c> on #8, which reads mwho(). Distinct from <see cref="ListAsync"/>, which is
	/// the roster of every character that exists: a character being listed there implies nothing
	/// about presence.
	/// </summary>
	public Task<ApiResult<IReadOnlyList<CharacterSummary>>> ListOnlineAsync(CancellationToken cancellationToken = default) =>
		Shared(_online, OnlineRoute, "Failed to load the online character list.", cancellationToken);

	/// <summary>
	/// A caller that has already given up is told so rather than handed a shared answer; one that gives up
	/// while waiting ends only its own wait.
	/// </summary>
	private Task<ApiResult<IReadOnlyList<CharacterSummary>>> Shared(ShortMemo<ApiResult<IReadOnlyList<CharacterSummary>>> memo,
		string route, string failureMessage, CancellationToken cancellationToken) =>
		cancellationToken.IsCancellationRequested
			? Task.FromCanceled<ApiResult<IReadOnlyList<CharacterSummary>>>(cancellationToken)
			: memo.GetAsync(() => FetchAsync(route, failureMessage)).WaitAsync(cancellationToken);

	/// <summary>
	/// The one request behind both reads. It runs without any caller's token because concurrent callers
	/// share it: a caller's cancellation ends only that caller's wait (<see cref="Task.WaitAsync(CancellationToken)"/>),
	/// and surfaces to it as an <see cref="OperationCanceledException"/>.
	/// </summary>
	private async Task<ApiResult<IReadOnlyList<CharacterSummary>>> FetchAsync(string route, string failureMessage)
	{
		var result = await httpClientFactory.CreateClient("api").GetTextApiAsync(route) switch
		{
			string body => Parse(body),
			ApiFailure failure => failure
		};

		if (result is ApiFailure failed)
			logger.LogWarning("{FailureMessage} {Reason}", failureMessage, failed.Message);

		return result;
	}

	/// <summary>
	/// The rows a successful answer carries.
	/// </summary>
	/// <remarks>
	/// These handlers are redefinable per game, so a malformed response is a configuration mistake
	/// rather than a bug here: it is a failure the page can show, not an exception that takes it down.
	/// Read as text rather than through <see cref="ApiCall.GetApiAsync{T}"/> because a <c>null</c>
	/// body is an empty directory here, not a failure. A request that exceeds
	/// <see cref="HttpClient.Timeout"/> is a <see cref="ApiFailureKind.Transport"/> failure like any
	/// other unanswered request; a caller's own cancellation never reaches here, because the shared
	/// request carries no caller's token.
	/// </remarks>
	private static ApiResult<IReadOnlyList<CharacterSummary>> Parse(string body)
	{
		try
		{
			return new ApiResult<IReadOnlyList<CharacterSummary>>(
				Normalize(JsonSerializer.Deserialize<List<CharacterSummary>>(body, JsonSerializerOptions.Web)));
		}
		catch (JsonException ex)
		{
			return ApiFailure.Malformed(ex);
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
				// A redefined handler may answer [null] or rows without an objid; neither is a character.
				.Where(r => r is { Objid: not null, Name: not null })
				.DistinctBy(r => r.Objid, StringComparer.Ordinal)
				.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

	/// <inheritdoc />
	public event Action? Changed;

	/// <summary>
	/// A gallery write changed a character's pictures: the next read asks the game again, and every avatar
	/// on screen is told to.
	/// </summary>
	public void PicturesChanged()
	{
		_roster.Forget();
		_online.Forget();
		Changed?.Invoke();
	}

	/// <inheritdoc />
	public async Task<string?> PictureOfAsync(string character, CancellationToken cancellationToken = default)
	{
		if (await ListAsync(cancellationToken) is not IReadOnlyList<CharacterSummary> rows) return null;

		var row = character.StartsWith('#')
			? rows.FirstOrDefault(r => character.Contains(':')
				? string.Equals(r.Objid, character, StringComparison.Ordinal)
				: string.Equals(r.Dbref, character, StringComparison.Ordinal))
			: rows.FirstOrDefault(r => string.Equals(r.Name, character, StringComparison.OrdinalIgnoreCase));
		return string.IsNullOrWhiteSpace(row?.Image) ? null : row.Image;
	}

	/// <summary>
	/// Resolves a character's objid by (case-insensitive) name via the directory.
	/// <see cref="NotFound"/> means no character answers to that name; an <see cref="ApiFailure"/> means
	/// the directory could not be read.
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
			ApiFailure failure => failure
		};
	}
}
