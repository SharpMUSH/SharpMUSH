using SharpMUSH.Client.Models;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side scene service. All reads go through the server REST API
/// (GET /api/scenes/...) — the WASM client has no local scene service. The portal
/// never writes scenes through this service: pose authoring happens via a normal
/// game command (POSE/SAY/SEMIPOSE) sent on the GameHub connection.
/// </summary>
public class SceneService(IHttpClientFactory httpClientFactory, ILogger<SceneService> logger)
{
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

	private record SceneMemberDto(
		string SceneId,
		string? MemberDbref,
		string MemberName,
		string Role,
		string ShowAs,
		bool IsCurrent,
		long GrantedAt);

	/// <summary>
	/// Lists scenes by filter (active|recent|scheduled). Failures (network, server
	/// error) return an empty list so the browse UI simply shows nothing.
	/// </summary>
	public async ValueTask<IReadOnlyList<SceneSummary>> ListScenesAsync(
		string filter = "recent", int count = 50)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<SceneDto>>(
				$"api/scenes?filter={Uri.EscapeDataString(filter)}&count={count}");
			return dtos?.Select(ToSummary).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "ListScenesAsync failed for filter={Filter}", filter);
			return [];
		}
	}

	/// <summary>Convenience: the currently running scenes.</summary>
	public ValueTask<IReadOnlyList<SceneSummary>> GetActiveScenesAsync(int count = 50)
		=> ListScenesAsync("active", count);

	/// <summary>Convenience: the most recent scenes (newest first).</summary>
	public ValueTask<IReadOnlyList<SceneSummary>> GetRecentScenesAsync(int count = 50)
		=> ListScenesAsync("recent", count);

	/// <summary>
	/// Returns one scene, or null when it does not exist or the caller may not see it.
	/// </summary>
	public async ValueTask<SceneSummary?> GetSceneAsync(string id)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dto = await http.GetFromJsonAsync<SceneDto>($"api/scenes/{Uri.EscapeDataString(id)}");
			return dto is null ? null : ToSummary(dto);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return null;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetSceneAsync failed for id={Id}", id);
			return null;
		}
	}

	/// <summary>
	/// Returns the scene's poses in chain order (optionally only the last
	/// <paramref name="count"/>). Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<ScenePoseView>> GetPosesAsync(string id, int? count = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var url = count is { } c
				? $"api/scenes/{Uri.EscapeDataString(id)}/poses?count={c}"
				: $"api/scenes/{Uri.EscapeDataString(id)}/poses";
			var dtos = await http.GetFromJsonAsync<List<ScenePoseDto>>(url);
			return dtos?.Select(ToPose).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetPosesAsync failed for id={Id}", id);
			return [];
		}
	}

	/// <summary>Returns the scene's members. Failures return an empty list.</summary>
	public async ValueTask<IReadOnlyList<SceneMemberView>> GetMembersAsync(string id)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<SceneMemberDto>>(
				$"api/scenes/{Uri.EscapeDataString(id)}/members");
			return dtos?.Select(ToMember).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetMembersAsync failed for id={Id}", id);
			return [];
		}
	}

	/// <summary>Returns the distinct display personas used in the scene. Failures return an empty list.</summary>
	public async ValueTask<IReadOnlyList<string>> GetCastAsync(string id)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var list = await http.GetFromJsonAsync<List<string>>(
				$"api/scenes/{Uri.EscapeDataString(id)}/cast");
			return list ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetCastAsync failed for id={Id}", id);
			return [];
		}
	}

	/// <summary>Returns the distinct opaque pose tags across the scene. Failures return an empty list.</summary>
	public async ValueTask<IReadOnlyList<string>> GetTagsAsync(string id)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var list = await http.GetFromJsonAsync<List<string>>(
				$"api/scenes/{Uri.EscapeDataString(id)}/tags");
			return list ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetTagsAsync failed for id={Id}", id);
			return [];
		}
	}

	private static SceneSummary ToSummary(SceneDto d) => new(
		d.Id, d.Status, d.IsPublic, d.IsTempRoom, d.ScheduledFor, d.StartedAt, d.LastActivityAt,
		d.PoseCount, d.OwnerDbref, d.OwnerName, d.StarterDbref, d.StarterName, d.RoomDbref, d.RoomName,
		d.Meta ?? new Dictionary<string, string>());

	private static ScenePoseView ToPose(ScenePoseDto d) => new(
		d.Id, d.SceneId, d.AuthorDbref, d.AuthorName, d.ShowAsName, d.OriginDbref, d.OriginName,
		d.Source, d.Tags ?? [], d.Meta ?? new Dictionary<string, string>(), d.CreatedAt, d.IsDeleted,
		d.Content, d.Markup, d.EditCount, d.LastEditedAt, d.LastEditorDbref, d.LastEditorName);

	private static SceneMemberView ToMember(SceneMemberDto d) => new(
		d.SceneId, d.MemberDbref, d.MemberName, d.Role, d.ShowAs, d.IsCurrent, d.GrantedAt);
}
