using System.Net.Http.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side scene service. All reads go through the server REST API
/// (GET /api/scenes/...) — the WASM client has no local scene service. The portal
/// never writes scenes through this service: pose authoring happens via a normal
/// game command (POSE/SAY/SEMIPOSE) sent on the GameHub connection.
/// </summary>
public class SceneService(IHttpClientFactory httpClientFactory, IAccountAuthState accountAuth)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>
	/// Raised when this tab knows the scene lists have changed — a scene it started has appeared — so
	/// views that read them once, such as the section sidebar, read them again.
	/// </summary>
	public event Action? Changed;

	/// <summary>
	/// Tells every view of the scene lists that they have changed. Reads from here on start their own
	/// requests: one already in flight may have been answered before the change.
	/// </summary>
	public void ReportChanged()
	{
		Interlocked.Increment(ref _changes);
		Changed?.Invoke();
	}

	/// <summary>How many changes have been reported; part of the key reads share a request under.</summary>
	private long _changes;

	/// <summary>
	/// Concurrent reads of one scene list (the stats tile and the active-scene widget) share a request —
	/// when they are made as the same acting character and fall on the same side of a reported change.
	/// The server filters these lists by who asks: a private scene is listed only to its owner and
	/// members, so one character's answer is not another's.
	/// </summary>
	private readonly SingleFlight<((int, long)? Acting, long Changes, string Url), ApiResult<IReadOnlyList<SceneSummary>>> _listFlight = new();

	/// <summary>The acting character the server answers a read for.</summary>
	private (int, long)? Acting => accountAuth.ActiveCharacter is { } acting ? (acting.DbrefNumber, acting.CreationTime) : null;

	// Mirror SceneController records; timestamps are long Unix-millis (deserialization contract).
	private record SceneDto(
		string Id,
		string Status,
		bool IsPublic,
		bool IsTempRoom,
		long? ScheduledFor,
		long StartedAt,
		long LastActivityAt,
		int PoseCount,
		string? OwnerDbref,
		string OwnerName,
		string? StarterDbref,
		string StarterName,
		string? RoomDbref,
		string RoomName,
		IReadOnlyDictionary<string, string> Meta);

	private record ScenePoseDto(
		string Id,
		string SceneId,
		string? AuthorDbref,
		string AuthorName,
		string ShowAsName,
		string? OriginDbref,
		string OriginName,
		string Source,
		IReadOnlyList<string> Tags,
		IReadOnlyDictionary<string, string> Meta,
		long CreatedAt,
		bool IsDeleted,
		string Content,
		string Markup,
		int EditCount,
		long? LastEditedAt,
		string? LastEditorDbref,
		string? LastEditorName,
		string? Type);

	private record SceneMemberDto(string? MemberDbref, bool IsCurrent);


	/// <summary>Lists scenes by filter (active|recent|scheduled).</summary>
	public Task<ApiResult<IReadOnlyList<SceneSummary>>> ListScenesAsync(string filter = "recent", int count = 50)
	{
		var url = $"api/scenes?filter={Uri.EscapeDataString(filter)}&count={count}";
		return _listFlight.RunAsync((Acting, Interlocked.Read(ref _changes), url), () => FetchScenesAsync(url));
	}

	private async Task<ApiResult<IReadOnlyList<SceneSummary>>> FetchScenesAsync(string url)
	{
		var result = await Client.GetApiAsync<List<SceneDto>>(url, "The server returned no scene list.");

		return result switch
		{
			List<SceneDto> dtos => (IReadOnlyList<SceneSummary>)[.. dtos.Select(ToSummary)],
			ApiFailure failure => failure
		};
	}

	/// <summary>
	/// The scenes <paramref name="dbref"/> belongs to that the caller may see, newest first: those in
	/// <paramref name="state"/> (<c>live</c>, <c>upcoming</c>, <c>finished</c>) whose title, pitch or room holds
	/// <paramref name="search"/>, after the first <paramref name="offset"/> of them.
	/// </summary>
	public async Task<ApiResult<IReadOnlyList<SceneSummary>>> GetParticipantScenesAsync(string dbref, int count = 5,
		int offset = 0, string? state = null, string? search = null)
	{
		var url = $"api/scenes?participant={Uri.EscapeDataString(dbref)}&count={count}";
		if (offset > 0) url += $"&offset={offset}";
		if (!string.IsNullOrWhiteSpace(state)) url += $"&state={Uri.EscapeDataString(state)}";
		if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search.Trim())}";
		var result = await Client.GetApiAsync<List<SceneDto>>(url, "The server returned no scene list.");

		return result switch
		{
			List<SceneDto> dtos => (IReadOnlyList<SceneSummary>)[.. dtos.Select(ToSummary)],
			ApiFailure failure => failure
		};
	}

	/// <summary>Who shares the most caller-visible scenes with <paramref name="dbref"/>, most first.</summary>
	public async Task<ApiResult<IReadOnlyList<ScenePartner>>> GetPartnersAsync(string dbref, int count = 6)
	{
		var result = await Client.GetApiAsync<List<ScenePartner>>(
			$"api/scenes/partners?participant={Uri.EscapeDataString(dbref)}&count={count}", "The server returned no partner list.");

		return result switch
		{
			List<ScenePartner> partners => (IReadOnlyList<ScenePartner>)partners,
			ApiFailure failure => failure
		};
	}

	/// <summary>
	/// The dbrefs (<c>#312</c>, without the creation stamp) of the scene's members — everyone who joined or
	/// watched it — skipping edges held by a recycled dbref's former owner.
	/// </summary>
	public async Task<ApiResult<IReadOnlyList<string>>> GetMemberDbrefsAsync(string sceneId)
	{
		var result = await Client.GetApiAsync<List<SceneMemberDto>>(
			$"api/scenes/{Uri.EscapeDataString(sceneId)}/members", "The server returned no member list.");

		return result switch
		{
			List<SceneMemberDto> members => (IReadOnlyList<string>)[.. members
				.Where(m => m.IsCurrent && !string.IsNullOrWhiteSpace(m.MemberDbref))
				.Select(m => m.MemberDbref!.Split(':')[0])
				.Distinct(StringComparer.Ordinal)],
			ApiFailure failure => failure
		};
	}

	/// <summary>Convenience: the currently running scenes.</summary>
	public Task<ApiResult<IReadOnlyList<SceneSummary>>> GetActiveScenesAsync(int count = 50)
		=> ListScenesAsync("active", count);

	/// <summary>Convenience: the most recent scenes (newest first).</summary>
	public Task<ApiResult<IReadOnlyList<SceneSummary>>> GetRecentScenesAsync(int count = 50)
		=> ListScenesAsync("recent", count);

	/// <summary>
	/// One scene. <see cref="ApiFailureKind.NotFound"/> covers both a scene that does not exist and one
	/// the caller may not see: the server answers 404 to both so that private scene ids cannot be probed.
	/// </summary>
	public async Task<ApiResult<SceneSummary>> GetSceneAsync(string id)
	{
		var result = await Client.GetApiAsync<SceneDto>($"api/scenes/{Uri.EscapeDataString(id)}", "The server returned no scene.");

		return result switch
		{
			SceneDto dto => ToSummary(dto),
			ApiFailure failure => failure
		};
	}

	/// <summary>
	/// The scene's poses in chain order (optionally only the last <paramref name="count"/>).
	/// </summary>
	public async Task<ApiResult<IReadOnlyList<ScenePoseView>>> GetPosesAsync(string id, int? count = null)
	{
		var url = count is { } c
			? $"api/scenes/{Uri.EscapeDataString(id)}/poses?count={c}"
			: $"api/scenes/{Uri.EscapeDataString(id)}/poses";

		var result = await Client.GetApiAsync<List<ScenePoseDto>>(url, "The server returned no poses.");

		return result switch
		{
			List<ScenePoseDto> dtos => (IReadOnlyList<ScenePoseView>)[.. dtos.Select(ToPose)],
			ApiFailure failure => failure
		};
	}

	/// <summary>How many streamed poses <see cref="StreamPosesAsync"/> hands over at a time.</summary>
	public const int PoseStreamBatch = 50;

	/// <summary>
	/// The scene's whole log in chain order, read as the server streams it: <paramref name="onBatch"/> gets
	/// each <see cref="PoseStreamBatch"/> poses as they arrive (and the remainder at the end), so a long log
	/// renders while it loads. Returns <see cref="Success"/> once the log ended, or the failure that stopped it
	/// — after which the batches already delivered are all there is.
	/// </summary>
	/// <remarks>
	/// Read with <c>GetFromJsonAsAsyncEnumerable</c>, which asks for the response as soon as its headers
	/// arrive. In the browser that streams only with WebAssembly response streaming on, which .NET 10 made the
	/// default for every request; nothing in this app turns it off.
	/// </remarks>
	public async Task<ApiResult<Success>> StreamPosesAsync(string id, Func<IReadOnlyList<ScenePoseView>, Task> onBatch,
		CancellationToken cancellationToken = default)
	{
		var batch = new List<ScenePoseView>(PoseStreamBatch);
		try
		{
			await foreach (var dto in Client.GetFromJsonAsAsyncEnumerable<ScenePoseDto>(
					$"api/scenes/{Uri.EscapeDataString(id)}/poses", cancellationToken))
			{
				if (dto is null) continue;
				batch.Add(ToPose(dto));
				if (batch.Count < PoseStreamBatch) continue;
				await onBatch(batch);
				batch = new List<ScenePoseView>(PoseStreamBatch);
			}

			if (batch.Count > 0) await onBatch(batch);
			return new Success();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (HttpRequestException ex) when (ex.StatusCode is { } status)
		{
			return ApiFailure.FromStatus(status, ex.Message);
		}
		catch (Exception ex)
		{
			return ApiFailure.Transport(ex);
		}
	}

	private static SceneSummary ToSummary(SceneDto d) => new(
		d.Id, d.Status, d.IsPublic, d.IsTempRoom, d.ScheduledFor, d.StartedAt, d.LastActivityAt,
		d.PoseCount, d.OwnerDbref, d.OwnerName, d.StarterDbref, d.StarterName, d.RoomDbref, d.RoomName,
		d.Meta ?? new Dictionary<string, string>());

	private static ScenePoseView ToPose(ScenePoseDto d) => new(
		d.Id, d.SceneId, d.AuthorDbref, d.AuthorName, d.ShowAsName, d.OriginDbref, d.OriginName,
		d.Source, d.Tags ?? [], d.Meta ?? new Dictionary<string, string>(), d.CreatedAt, d.IsDeleted,
		d.Content, d.Markup, d.EditCount, d.LastEditedAt, d.LastEditorDbref, d.LastEditorName,
		string.IsNullOrWhiteSpace(d.Type) ? PoseTypeInfo.InCharacter : d.Type);
}
