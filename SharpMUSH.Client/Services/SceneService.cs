using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side scene service. All reads go through the server REST API
/// (GET /api/scenes/...) — the WASM client has no local scene service. The portal
/// never writes scenes through this service: pose authoring happens via a normal
/// game command (POSE/SAY/SEMIPOSE) sent on the GameHub connection.
/// </summary>
public class SceneService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>
	/// Raised when this tab knows the scene lists have changed — a scene it started has appeared — so
	/// views that read them once, such as the section sidebar, read them again.
	/// </summary>
	public event Action? Changed;

	/// <summary>Tells every view of the scene lists that they have changed.</summary>
	public void ReportChanged() => Changed?.Invoke();

	/// <summary>Concurrent reads of one scene list (the stats tile and the active-scene widget) share a request.</summary>
	private readonly SingleFlight<string, ApiResult<IReadOnlyList<SceneSummary>>> _listFlight = new();

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
		string? LastEditorName);

	private record SceneMemberDto(string? MemberDbref, bool IsCurrent);


	/// <summary>Lists scenes by filter (active|recent|scheduled).</summary>
	public Task<ApiResult<IReadOnlyList<SceneSummary>>> ListScenesAsync(string filter = "recent", int count = 50)
	{
		var url = $"api/scenes?filter={Uri.EscapeDataString(filter)}&count={count}";
		return _listFlight.RunAsync(url, () => FetchScenesAsync(url));
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

	/// <summary>The scenes <paramref name="dbref"/> belongs to that the caller may see, newest first.</summary>
	public async Task<ApiResult<IReadOnlyList<SceneSummary>>> GetParticipantScenesAsync(string dbref, int count = 5)
	{
		var result = await Client.GetApiAsync<List<SceneDto>>(
			$"api/scenes?participant={Uri.EscapeDataString(dbref)}&count={count}", "The server returned no scene list.");

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

	private static SceneSummary ToSummary(SceneDto d) => new(
		d.Id, d.Status, d.IsPublic, d.IsTempRoom, d.ScheduledFor, d.StartedAt, d.LastActivityAt,
		d.PoseCount, d.OwnerDbref, d.OwnerName, d.StarterDbref, d.StarterName, d.RoomDbref, d.RoomName,
		d.Meta ?? new Dictionary<string, string>());

	private static ScenePoseView ToPose(ScenePoseDto d) => new(
		d.Id, d.SceneId, d.AuthorDbref, d.AuthorName, d.ShowAsName, d.OriginDbref, d.OriginName,
		d.Source, d.Tags ?? [], d.Meta ?? new Dictionary<string, string>(), d.CreatedAt, d.IsDeleted,
		d.Content, d.Markup, d.EditCount, d.LastEditedAt, d.LastEditorDbref, d.LastEditorName);
}
