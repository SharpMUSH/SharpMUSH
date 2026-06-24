using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SceneModel = SharpMUSH.Plugins.Scene.Models.Scene;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net.Models;
using System.Text.Json;
using OkNone = OneOf.Types.None;

namespace SharpMUSH.Plugins.Scene.Storage;

// IMPORTANT: SurrealDb.Net's embedded CBOR serializer ignores [JsonPropertyName].
// Every *DbRecord property name below MUST be the stored camelCase field name verbatim
// (e.g. sceneId, authorName, showAsName, isDeleted, tags, meta, createdAt). See
// surrealdb-net-deserialization. `Meta` and `tags` are stored as a JSON string / list of
// strings respectively because the inline-parameter ExecuteAsync helper cannot serialize a
// Dictionary<string, object?> directly (cf. how `locks` is persisted as a JSON string).
//
// Graph-native design (docs/design/scene-system.md): object references are RELATE edges to
// the live game-object record (object:<key>) PLUS a *Name snapshot field on the scene-side
// record. Pose order is the first_pose/last_pose + pose_next linked list. Pose content is
// versioned in scene_pose_edit records linked by first_edit + next_edit, with a current_edit
// pointer. Undo/redo move current_edit; a new edit truncates forward versions.
//
// Table-name mapping follows the Wiki convention (node_*/edge_* constants -> short snake_case
// table names): scene, scene_pose, scene_pose_edit, scene_plot and the scene_* edge tables.

internal class SceneDbRecord : Record
{
	public string status { get; set; } = "new";
	public bool isPublic { get; set; }
	public bool isTempRoom { get; set; }
	public long? scheduledFor { get; set; }
	public long startedAt { get; set; }
	public long lastActivityAt { get; set; }
	public int poseCount { get; set; }
	public string ownerName { get; set; } = "";
	public string starterName { get; set; } = "";
	public string roomName { get; set; } = "";
	// Opaque key/value bag persisted as a JSON string (see header note).
	public string? meta { get; set; }
}

internal class ScenePoseDbRecord : Record
{
	public string authorName { get; set; } = "";
	public string showAsName { get; set; } = "";
	public string originName { get; set; } = "";
	public string source { get; set; } = "";
	public List<string>? tags { get; set; }
	public string? meta { get; set; }
	public long createdAt { get; set; }
	public bool isDeleted { get; set; }
}

internal class ScenePoseEditDbRecord : Record
{
	public string content { get; set; } = "";
	public string markup { get; set; } = "";
	public string editorName { get; set; } = "";
	public long editedAt { get; set; }
}

internal class ScenePlotDbRecord : Record
{
	public string title { get; set; } = "";
	public string description { get; set; } = "";
	public string ownerName { get; set; } = "";
	public long createdAt { get; set; }
	public long updatedAt { get; set; }
}

// Minimal projection of the live game object for name-snapshot reads (SELECT name FROM object:<key>).
// Field name MUST match the stored camelCase verbatim (CBOR serializer ignores [JsonPropertyName]).
internal class SceneObjectNameRecord
{
	public string name { get; set; } = "";
}

/// <summary>
/// SurrealDB storage for the graph-native SceneModel System, relocated out of the core SurrealDB provider into
/// the SceneModel plugin (Phase 8). The provider's query entry points (parameter-inlining + escaping) and id
/// helper arrive through the host-shared <see cref="ISurrealStorageAccessor"/>; the SurrealQL is verbatim.
/// </summary>
public sealed class SurrealSceneStorage(ISurrealStorageAccessor _accessor) : ISceneStorage
{
	// Mirror of the provider's JsonOptions (the meta bag is (de)serialized as a JSON string).
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = null,
		WriteIndented = false
	};

	#region SceneModel

	private const string SceneFields =
		"id, status, isPublic, isTempRoom, scheduledFor, startedAt, lastActivityAt, poseCount, " +
		"ownerName, starterName, roomName, meta";

	private const string ScenePoseFields =
		"id, authorName, showAsName, originName, source, tags, meta, createdAt, isDeleted";

	private const string ScenePoseEditFields =
		"id, content, markup, editorName, editedAt";

	private const string ScenePlotFields =
		"id, title, description, ownerName, createdAt, updatedAt";

	public async Task<SceneModel> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "")
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var sceneId = (await GetNextSceneIdAsync()).ToString();

		var ownerName = await ResolveObjectNameAsync(ownerDbref);
		var roomName = await ResolveObjectNameAsync(roomDbref);
		// Starter defaults to the owner.
		var starterName = ownerName;

		var meta = new Dictionary<string, string>();
		if (!string.IsNullOrEmpty(title))
			meta["title"] = title;

		var parameters = new Dictionary<string, object?>
		{
			["id"] = sceneId,
			["status"] = "new", // create-default must match the other providers (ArangoDB/Memgraph) — a freshly created scene is "new", not "active"

			["startedAt"] = now,
			["lastActivityAt"] = now,
			["ownerName"] = ownerName ?? "",
			["starterName"] = starterName ?? "",
			["roomName"] = roomName ?? "",
			["meta"] = JsonSerializer.Serialize(meta, JsonOptions)
		};

		await _accessor.ExecuteAsync("""
			CREATE scene:⟨$id⟩ SET
				status = $status,
				isPublic = false,
				isTempRoom = false,
				scheduledFor = NONE,
				startedAt = $startedAt,
				lastActivityAt = $lastActivityAt,
				poseCount = 0,
				ownerName = $ownerName,
				starterName = $starterName,
				roomName = $roomName,
				meta = $meta
			""",
			parameters);

		await RelateSceneToObjectAsync("scene_owner", sceneId, ownerDbref);
		await RelateSceneToObjectAsync("scene_starter", sceneId, ownerDbref);
		if (DbRefToKey(roomDbref) is not null)
			await RelateSceneToObjectAsync("scene_in_room", sceneId, roomDbref);

		var result = await GetSceneAsync($"scene:{sceneId}");
		// CreateScene cannot miss its own freshly-created record; fall back to a projection.
		return result.IsT0
			? result.AsT0
			: ProjectScene(new SceneDbRecord
			{
				status = "active",
				startedAt = now,
				lastActivityAt = now,
				ownerName = ownerName ?? "",
				starterName = starterName ?? "",
				roomName = roomName ?? "",
				meta = JsonSerializer.Serialize(meta, JsonOptions)
			}, $"scene:{sceneId}", DbRefToString(ownerDbref), DbRefToString(ownerDbref), DbRefToString(roomDbref));
	}

	public async Task<OneOf<SceneModel, NotFound>> GetSceneAsync(string sceneId)
	{
		var keySegment = SceneKey(sceneId).Split(':')[1];
		var parameters = new Dictionary<string, object?> { ["k"] = keySegment };
		var response = await _accessor.ExecuteAsync($"SELECT {SceneFields} FROM scene:⟨$k⟩", parameters);
		var rows = response.GetValue<List<SceneDbRecord>>(0);
		if (rows is null or { Count: 0 })
			return new NotFound();

		var rec = rows[0];
		var idKey = NormalizeSceneId(rec.Id);
		var (ownerDbref, _) = await ResolveSceneObjectEdgeAsync("scene_owner", idKey);
		var (starterDbref, _) = await ResolveSceneObjectEdgeAsync("scene_starter", idKey);
		var (roomDbref, _) = await ResolveSceneObjectEdgeAsync("scene_in_room", idKey);
		return ProjectScene(rec, idKey, ownerDbref, starterDbref, roomDbref);
	}

	public async Task<OneOf<SceneModel, NotFound>> SetSceneMetaAsync(string sceneId, string key, string value)
	{
		var existing = await GetSceneAsync(sceneId);
		if (existing.IsT1)
			return new NotFound();

		var sceneKey = SceneKey(sceneId);
		var idParam = Rid(sceneKey);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var normalized = key.Trim().ToLowerInvariant();

		switch (normalized)
		{
			case "status":
				await _accessor.ExecuteAsync("UPDATE $id MERGE { status: $v, lastActivityAt: $now }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = value, ["now"] = now });
				break;
			case "public":
				await _accessor.ExecuteAsync("UPDATE $id MERGE { isPublic: $v, lastActivityAt: $now }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = ParseBool(value), ["now"] = now });
				break;
			case "istemp":
				await _accessor.ExecuteAsync("UPDATE $id MERGE { isTempRoom: $v, lastActivityAt: $now }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = ParseBool(value), ["now"] = now });
				break;
			case "scheduledfor":
				var sched = long.TryParse(value, out var ms) ? (long?)ms : null;
				await _accessor.ExecuteAsync("UPDATE $id MERGE { scheduledFor: $v, lastActivityAt: $now }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = sched, ["now"] = now });
				break;
			case "room":
				await ReplaceSceneObjectEdgeAsync("scene_in_room", sceneKey, value);
				await _accessor.ExecuteAsync("UPDATE $id MERGE { roomName: $name, lastActivityAt: $now }",
					new Dictionary<string, object?> { ["id"] = idParam, ["name"] = await ResolveObjectNameAsync(value) ?? "", ["now"] = now });
				break;
			case "owner":
				await ReplaceSceneObjectEdgeAsync("scene_owner", sceneKey, value);
				await _accessor.ExecuteAsync("UPDATE $id MERGE { ownerName: $name, lastActivityAt: $now }",
					new Dictionary<string, object?> { ["id"] = idParam, ["name"] = await ResolveObjectNameAsync(value) ?? "", ["now"] = now });
				break;
			case "plot":
				// Treat the value as a plot id and link the scene under it.
				await LinkSceneToPlotAsync(value, sceneId);
				await _accessor.ExecuteAsync("UPDATE $id MERGE { lastActivityAt: $now }",
					new Dictionary<string, object?> { ["id"] = idParam, ["now"] = now });
				break;
			default:
				// Known descriptive keys (title, summary, icdate, location, type, warning)
				// and any custom key land in the opaque Meta bag.
				await UpdateSceneMetaKeyAsync(sceneKey, normalized, value, now);
				break;
		}

		return await GetSceneAsync(sceneId);
	}

	public async Task<IReadOnlyList<SceneModel>> ListScenesAsync(string filter, string? viewerDbref = null,
		long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50)
	{
		var normalized = (filter ?? "").Trim().ToLowerInvariant();
		var parameters = new Dictionary<string, object?> { ["count"] = count };
		string query;

		switch (normalized)
		{
			case "scheduled":
				var bounds = "scheduledFor != NONE";
				if (fromUtcMillis is not null)
				{
					bounds += " AND scheduledFor >= $from";
					parameters["from"] = fromUtcMillis.Value;
				}
				if (toUtcMillis is not null)
				{
					bounds += " AND scheduledFor <= $to";
					parameters["to"] = toUtcMillis.Value;
				}
				query = $"SELECT {SceneFields} FROM scene WHERE {bounds} ORDER BY scheduledFor ASC LIMIT $count";
				break;
			case "mine":
				// Scenes where the viewer is a member (member edge) — recent-first.
				var viewerKey = DbRefToKey(viewerDbref);
				if (viewerKey is null)
					return Array.Empty<SceneModel>();
				parameters["pk"] = viewerKey.Value;
				query = $"SELECT {SceneFields} FROM scene WHERE id IN " +
						"(SELECT VALUE out FROM scene_member WHERE in = object:$pk) " +
						"ORDER BY lastActivityAt DESC LIMIT $count";
				break;
			case "active":
				query = $"SELECT {SceneFields} FROM scene WHERE status = 'active' ORDER BY lastActivityAt DESC LIMIT $count";
				break;
			case "finished":
				query = $"SELECT {SceneFields} FROM scene WHERE status = 'finished' ORDER BY lastActivityAt DESC LIMIT $count";
				break;
			case "recent":
			default:
				query = $"SELECT {SceneFields} FROM scene ORDER BY lastActivityAt DESC LIMIT $count";
				break;
		}

		var response = await _accessor.ExecuteAsync(query, parameters);
		var rows = response.GetValue<List<SceneDbRecord>>(0) ?? [];
		return await ProjectScenesAsync(rows);
	}

	public async Task<OneOf<SceneModel, NotFound>> GetActiveSceneInRoomAsync(string roomDbref)
	{
		var roomKey = DbRefToKey(roomDbref);
		if (roomKey is null)
			return new NotFound();

		var parameters = new Dictionary<string, object?> { ["rk"] = roomKey.Value };
		var response = await _accessor.ExecuteAsync(
			$"SELECT {SceneFields} FROM scene WHERE status = 'active' AND id IN " +
			"(SELECT VALUE in FROM scene_in_room WHERE out = object:$rk) " +
			"ORDER BY lastActivityAt DESC LIMIT 1",
			parameters);
		var rows = response.GetValue<List<SceneDbRecord>>(0);
		if (rows is null or { Count: 0 })
			return new NotFound();

		var projected = await ProjectScenesAsync(rows);
		return projected[0];
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> AddPoseAsync(string sceneId, string authorDbref,
		string showAs, string originDbref, string source, IReadOnlyList<string> tags, string content)
	{
		var sceneExisting = await GetSceneAsync(sceneId);
		if (sceneExisting.IsT1)
			return new NotFound();

		var sceneKey = SceneKey(sceneId);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var poseId = (await GetNextPoseIdAsync()).ToString();
		var editId = Guid.NewGuid().ToString("N");

		var authorName = await ResolveObjectNameAsync(authorDbref) ?? "";
		var originName = await ResolveObjectNameAsync(originDbref) ?? "";
		var tagList = (tags ?? []).ToList();
		var plain = StripMarkup(content);

		await _accessor.ExecuteAsync("""
			CREATE scene_pose:⟨$id⟩ SET
				authorName = $authorName,
				showAsName = $showAs,
				originName = $originName,
				source = $source,
				tags = $tags,
				meta = $meta,
				createdAt = $now,
				isDeleted = false
			""",
			new Dictionary<string, object?>
			{
				["id"] = poseId,
				["authorName"] = authorName,
				["showAs"] = showAs ?? "",
				["originName"] = originName,
				["source"] = source ?? "",
				["tags"] = tagList,
				["meta"] = JsonSerializer.Serialize(new Dictionary<string, string>(), JsonOptions),
				["now"] = now
			});

		await _accessor.ExecuteAsync("""
			CREATE scene_pose_edit:⟨$id⟩ SET
				content = $content,
				markup = $markup,
				editorName = $editorName,
				editedAt = $now
			""",
			new Dictionary<string, object?>
			{
				["id"] = editId,
				["content"] = plain,
				["markup"] = content ?? "",
				["editorName"] = authorName,
				["now"] = now
			});

		await RelatePoseToSceneAsync(poseId, sceneKey);
		await RelatePoseToEditAsync("scene_first_edit", poseId, editId);
		await RelatePoseToEditAsync("scene_current_edit", poseId, editId);
		await RelatePoseToObjectAsync("scene_pose_author", poseId, authorDbref);
		if (DbRefToKey(originDbref) is not null)
			await RelatePoseToObjectAsync("scene_pose_origin", poseId, originDbref);
		await RelateEditToObjectAsync("scene_edit_editor", editId, authorDbref);

		await AppendPoseToChainAsync(sceneKey, poseId);

		await _accessor.ExecuteAsync(
			"UPDATE $id SET poseCount = poseCount + 1, lastActivityAt = $now",
			new Dictionary<string, object?> { ["id"] = Rid(sceneKey), ["now"] = now });

		var pose = await GetPoseAsync($"scene_pose:{poseId}");
		if (pose.IsT1)
			return new Error<string>("Database returned empty result after pose insert.");
		return pose.AsT0;
	}

	public async Task<OneOf<ScenePose, NotFound>> GetPoseAsync(string poseId)
	{
		var key = PoseKey(poseId);
		var response = await _accessor.ExecuteAsync($"SELECT {ScenePoseFields} FROM $id",
			new Dictionary<string, object?> { ["id"] = Rid(key) });		var rows = response.GetValue<List<ScenePoseDbRecord>>(0);
		if (rows is null or { Count: 0 })
			return new NotFound();

		return await ProjectPoseAsync(rows[0]);
	}

	public async Task<OneOf<IReadOnlyList<ScenePose>, NotFound>> GetPosesAsync(string sceneId,
		string? authorDbref = null, int? count = null)
	{
		var sceneExisting = await GetSceneAsync(sceneId);
		if (sceneExisting.IsT1)
			return new NotFound();

		var sceneKey = SceneKey(sceneId);
		var orderedKeys = await TraversePoseChainAsync(sceneKey);

		var authorKey = DbRefToKey(authorDbref);
		var result = new List<ScenePose>();
		foreach (var poseKey in orderedKeys)
		{
			var pose = await GetPoseAsync(poseKey);
			if (!pose.IsT0)
				continue;
			var p = pose.AsT0;
			if (authorKey is not null)
			{
				if (p.AuthorDbref != $"#{authorKey.Value}")
					continue;
			}
			result.Add(p);
		}

		if (count is not null && count.Value >= 0 && result.Count > count.Value)
			result = result.Skip(result.Count - count.Value).ToList();

		return OneOf<IReadOnlyList<ScenePose>, NotFound>.FromT0(result);
	}

	public async Task<OneOf<ScenePose, NotFound>> SetPoseMetaAsync(string poseId, string key, string value)
	{
		var existing = await GetPoseAsync(poseId);
		if (existing.IsT1)
			return new NotFound();

		var poseKey = PoseKey(poseId);
		var idParam = Rid(poseKey);
		var normalized = key.Trim().ToLowerInvariant();

		switch (normalized)
		{
			case "showas":
				await _accessor.ExecuteAsync("UPDATE $id MERGE { showAsName: $v }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = value });
				break;
			case "authorname":
				await _accessor.ExecuteAsync("UPDATE $id MERGE { authorName: $v }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = value });
				break;
			case "author":
				await ReplacePoseObjectEdgeAsync("scene_pose_author", poseKey, value);
				await _accessor.ExecuteAsync("UPDATE $id MERGE { authorName: $v }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = await ResolveObjectNameAsync(value) ?? "" });
				break;
			case "origin":
				await ReplacePoseObjectEdgeAsync("scene_pose_origin", poseKey, value);
				await _accessor.ExecuteAsync("UPDATE $id MERGE { originName: $v }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = await ResolveObjectNameAsync(value) ?? "" });
				break;
			case "originname":
				await _accessor.ExecuteAsync("UPDATE $id MERGE { originName: $v }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = value });
				break;
			case "source":
				await _accessor.ExecuteAsync("UPDATE $id MERGE { source: $v }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = value });
				break;
			case "tags":
				var tags = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
				await _accessor.ExecuteAsync("UPDATE $id MERGE { tags: $v }",
					new Dictionary<string, object?> { ["id"] = idParam, ["v"] = tags });
				break;
			default:
				await UpdatePoseMetaKeyAsync(poseKey, normalized, value);
				break;
		}

		return await GetPoseAsync(poseId);
	}

	public async Task<OneOf<ScenePose, NotFound>> EditPoseAsync(string poseId, string editorDbref, string content)
	{
		var existing = await GetPoseAsync(poseId);
		if (existing.IsT1)
			return new NotFound();

		var poseKey = PoseKey(poseId);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var editId = Guid.NewGuid().ToString("N");
		var editorName = await ResolveObjectNameAsync(editorDbref) ?? "";
		var plain = StripMarkup(content);

		var currentEdit = await ResolveEditPointerAsync("scene_current_edit", poseKey);

		// Truncate any redo-forward versions: drop edits after the current pointer.
		if (currentEdit is not null)
			await TruncateForwardEditsAsync(currentEdit);

		await _accessor.ExecuteAsync("""
			CREATE scene_pose_edit:⟨$id⟩ SET
				content = $content,
				markup = $markup,
				editorName = $editorName,
				editedAt = $now
			""",
			new Dictionary<string, object?>
			{
				["id"] = editId,
				["content"] = plain,
				["markup"] = content ?? "",
				["editorName"] = editorName,
				["now"] = now
			});
		await RelateEditToObjectAsync("scene_edit_editor", editId, editorDbref);

		if (currentEdit is not null)
			await RelateEditToEditAsync("scene_next_edit", EditKeyToId(currentEdit), editId);
		else
			await RelatePoseToEditAsync("scene_first_edit", poseId, editId);

		await RepointEditPointerAsync("scene_current_edit", poseKey, editId);

		return await GetPoseAsync(poseId);
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> UndoPoseAsync(string poseId)
	{
		var existing = await GetPoseAsync(poseId);
		if (existing.IsT1)
			return new NotFound();

		var poseKey = PoseKey(poseId);
		var chain = await TraverseEditChainAsync(poseKey);
		var current = await ResolveEditPointerAsync("scene_current_edit", poseKey);
		if (current is null || chain.Count == 0)
			return new Error<string>("No edit history for this pose.");

		var idx = chain.IndexOf(current);
		if (idx <= 0)
			return new Error<string>("Already at the oldest version.");

		await RepointEditPointerByKeyAsync("scene_current_edit", poseKey, chain[idx - 1]);
		var refreshed = await GetPoseAsync(poseId);
		if (refreshed.IsT1)
			return new NotFound();
		return refreshed.AsT0;
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> RedoPoseAsync(string poseId)
	{
		var existing = await GetPoseAsync(poseId);
		if (existing.IsT1)
			return new NotFound();

		var poseKey = PoseKey(poseId);
		var chain = await TraverseEditChainAsync(poseKey);
		var current = await ResolveEditPointerAsync("scene_current_edit", poseKey);
		if (current is null || chain.Count == 0)
			return new Error<string>("No edit history for this pose.");

		var idx = chain.IndexOf(current);
		if (idx < 0 || idx >= chain.Count - 1)
			return new Error<string>("Already at the newest version.");

		await RepointEditPointerByKeyAsync("scene_current_edit", poseKey, chain[idx + 1]);
		var refreshed = await GetPoseAsync(poseId);
		if (refreshed.IsT1)
			return new NotFound();
		return refreshed.AsT0;
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> MovePoseAsync(string poseId, string afterPoseId)
	{
		var existing = await GetPoseAsync(poseId);
		if (existing.IsT1)
			return new NotFound();

		var poseKey = PoseKey(poseId);
		var sceneKey = await ResolvePoseSceneKeyAsync(poseKey);
		if (sceneKey is null)
			return new Error<string>("Pose is not attached to a scene.");

		string? afterKey = null;
		if (!string.IsNullOrWhiteSpace(afterPoseId))
		{
			afterKey = PoseKey(afterPoseId);
			var afterScene = await ResolvePoseSceneKeyAsync(afterKey);
			if (afterScene is null || !string.Equals(afterScene, sceneKey, StringComparison.Ordinal))
				return new Error<string>("The two poses are not in the same scene.");
			if (string.Equals(afterKey, poseKey, StringComparison.Ordinal))
				return new Error<string>("Cannot move a pose after itself.");
		}

		await UnlinkPoseAsync(sceneKey, poseKey);
		await InsertPoseAfterAsync(sceneKey, poseKey, afterKey);

		await _accessor.ExecuteAsync("UPDATE $id SET lastActivityAt = $now",
			new Dictionary<string, object?>
			{
				["id"] = Rid(sceneKey),
				["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});

		var refreshed = await GetPoseAsync(poseId);
		if (refreshed.IsT1)
			return new NotFound();
		return refreshed.AsT0;
	}

	public async Task<OneOf<ScenePose, NotFound>> DeletePoseAsync(string poseId)
	{
		var existing = await GetPoseAsync(poseId);
		if (existing.IsT1)
			return new NotFound();

		var poseKey = PoseKey(poseId);
		await _accessor.ExecuteAsync("UPDATE $id MERGE { isDeleted: true }",
			new Dictionary<string, object?> { ["id"] = Rid(poseKey) });
		var sceneKey = await ResolvePoseSceneKeyAsync(poseKey);
		if (sceneKey is not null)
			await _accessor.ExecuteAsync(
				"UPDATE $id SET poseCount = math::max([0, poseCount - 1]), lastActivityAt = $now",
				new Dictionary<string, object?>
				{
					["id"] = Rid(sceneKey),
					["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				});

		return await GetPoseAsync(poseId);
	}

	public async Task<OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>> GetPoseEditsAsync(string poseId)
	{
		var existing = await GetPoseAsync(poseId);
		if (existing.IsT1)
			return new NotFound();

		var poseKey = PoseKey(poseId);
		var chain = await TraverseEditChainAsync(poseKey);
		var result = new List<ScenePoseEdit>();
		foreach (var editKey in chain)
		{
			var edit = await ReadEditAsync(editKey, poseKey);
			if (edit is not null)
				result.Add(edit);
		}

		return OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>.FromT0(result);
	}

	public async Task<OneOf<SceneMember, NotFound>> AddMemberAsync(string sceneId, string playerDbref, string role)
	{
		var sceneExisting = await GetSceneAsync(sceneId);
		if (sceneExisting.IsT1)
			return new NotFound();

		var playerKey = DbRefToKey(playerDbref);
		if (playerKey is null)
			return new NotFound();

		var sceneKey = SceneKey(sceneId);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var memberName = await ResolveObjectNameAsync(playerDbref) ?? "";

		// At most one member edge per (player, scene): delete then RELATE.
		await _accessor.ExecuteAsync(
			"DELETE scene_member WHERE in = object:$pk AND out = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["pk"] = playerKey.Value, ["sid"] = sceneKey.Split(':')[1] });

		await _accessor.ExecuteAsync(
			"RELATE object:$pk->scene_member->scene:⟨$sid⟩ SET " +
			"role = $role, showAs = '', isCurrent = false, grantedAt = $now, memberName = $name",
			new Dictionary<string, object?>
			{
				["pk"] = playerKey.Value,
				["sid"] = sceneKey.Split(':')[1],
				["role"] = role ?? "",
				["now"] = now,
				["name"] = memberName
			});

		return await GetMemberAsync(sceneId, playerDbref);
	}

	public async Task<OneOf<OkNone, NotFound>> RemoveMemberAsync(string sceneId, string playerDbref)
	{
		var sceneExisting = await GetSceneAsync(sceneId);
		if (sceneExisting.IsT1)
			return new NotFound();

		var playerKey = DbRefToKey(playerDbref);
		if (playerKey is null)
			return new OkNone();

		var sceneKey = SceneKey(sceneId);
		await _accessor.ExecuteAsync(
			"DELETE scene_member WHERE in = object:$pk AND out = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["pk"] = playerKey.Value, ["sid"] = sceneKey.Split(':')[1] });

		return new OkNone();
	}

	public async Task<OneOf<IReadOnlyList<SceneMember>, NotFound>> GetMembersAsync(string sceneId, string? role = null)
	{
		var sceneExisting = await GetSceneAsync(sceneId);
		if (sceneExisting.IsT1)
			return new NotFound();

		var sceneKey = SceneKey(sceneId);
		var where = "out = scene:⟨$sid⟩";
		var parameters = new Dictionary<string, object?> { ["sid"] = sceneKey.Split(':')[1] };
		if (!string.IsNullOrWhiteSpace(role))
		{
			where += " AND role = $role";
			parameters["role"] = role;
		}

		var response = await _accessor.ExecuteAsync(
			$"SELECT role, showAs, isCurrent, grantedAt, memberName, in.key AS memberKey FROM scene_member WHERE {where}",
			parameters);
		var rows = response.GetValue<List<SceneMemberEdgeRecord>>(0) ?? [];
		var members = rows.Select(r => ProjectMember(r, sceneKey)).ToList();
		return OneOf<IReadOnlyList<SceneMember>, NotFound>.FromT0(members);
	}

	public async Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref)
	{
		var sceneExisting = await GetSceneAsync(sceneId);
		if (sceneExisting.IsT1)
			return new NotFound();

		var playerKey = DbRefToKey(playerDbref);
		if (playerKey is null)
			return new NotFound();

		var sceneKey = SceneKey(sceneId);
		var response = await _accessor.ExecuteAsync(
			"SELECT role, showAs, isCurrent, grantedAt, memberName, in.key AS memberKey FROM scene_member " +
			"WHERE in = object:$pk AND out = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["pk"] = playerKey.Value, ["sid"] = sceneKey.Split(':')[1] });
		var rows = response.GetValue<List<SceneMemberEdgeRecord>>(0);
		if (rows is null or { Count: 0 })
			return new NotFound();

		return ProjectMember(rows[0], sceneKey);
	}

	public async Task<OneOf<OkNone, NotFound>> SetFocusAsync(string playerDbref, string? sceneId = null)
	{
		var playerKey = DbRefToKey(playerDbref);
		if (playerKey is null)
			return new NotFound();

		// Clear isCurrent on all of the player's member edges first.
		await _accessor.ExecuteAsync(
			"UPDATE scene_member SET isCurrent = false WHERE in = object:$pk",
			new Dictionary<string, object?> { ["pk"] = playerKey.Value });

		if (string.IsNullOrWhiteSpace(sceneId))
			return new OkNone();

		var sceneExisting = await GetSceneAsync(sceneId);
		if (sceneExisting.IsT1)
			return new NotFound();

		var sceneKey = SceneKey(sceneId);
		var sid = sceneKey.Split(':')[1];

		// Ensure a member edge exists, then mark it current — matches ArangoDB/Memgraph: focusing a
		// player who is not yet a member auto-creates a role-less member edge so the focus sticks (a bare
		// UPDATE would no-op for a non-member, leaving the player with no current scene).
		var member = await GetMemberAsync(sceneId, playerDbref);
		if (member.IsT1)
		{
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var memberName = await ResolveObjectNameAsync(playerDbref) ?? "";
			await _accessor.ExecuteAsync(
				"RELATE object:$pk->scene_member->scene:⟨$sid⟩ SET " +
				"role = '', showAs = '', isCurrent = true, grantedAt = $now, memberName = $name",
				new Dictionary<string, object?>
				{
					["pk"] = playerKey.Value,
					["sid"] = sid,
					["now"] = now,
					["name"] = memberName
				});
		}
		else
		{
			await _accessor.ExecuteAsync(
				"UPDATE scene_member SET isCurrent = true WHERE in = object:$pk AND out = scene:⟨$sid⟩",
				new Dictionary<string, object?> { ["pk"] = playerKey.Value, ["sid"] = sid });
		}

		return new OkNone();
	}

	public async Task<OneOf<SceneModel, NotFound>> GetCurrentSceneAsync(string playerDbref)
	{
		var playerKey = DbRefToKey(playerDbref);
		if (playerKey is null)
			return new NotFound();

		var response = await _accessor.ExecuteAsync(
			"SELECT VALUE meta::id(out) FROM scene_member WHERE in = object:$pk AND isCurrent = true LIMIT 1",
			new Dictionary<string, object?> { ["pk"] = playerKey.Value });
		var ids = response.GetValue<List<string>>(0);
		if (ids is null or { Count: 0 })
			return new NotFound();

		return await GetSceneAsync($"scene:{ids[0]}");
	}

	public async Task<OneOf<SceneMember, NotFound>> SetShowAsAsync(string sceneId, string playerDbref, string showAs)
	{
		var member = await GetMemberAsync(sceneId, playerDbref);
		if (member.IsT1)
			return new NotFound();

		var playerKey = DbRefToKey(playerDbref);
		var sceneKey = SceneKey(sceneId);
		await _accessor.ExecuteAsync(
			"UPDATE scene_member SET showAs = $v WHERE in = object:$pk AND out = scene:⟨$sid⟩",
			new Dictionary<string, object?>
			{
				["v"] = showAs ?? "",
				["pk"] = playerKey!.Value,
				["sid"] = sceneKey.Split(':')[1]
			});

		return await GetMemberAsync(sceneId, playerDbref);
	}

	public async Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var ownerName = await ResolveObjectNameAsync(ownerDbref) ?? "";

		if (string.IsNullOrWhiteSpace(plotId))
		{
			var newId = Guid.NewGuid().ToString("N");
			await _accessor.ExecuteAsync("""
				CREATE scene_plot:⟨$id⟩ SET
					title = $title, description = $description,
					ownerName = $ownerName, createdAt = $now, updatedAt = $now
				""",
				new Dictionary<string, object?>
				{
					["id"] = newId,
					["title"] = title ?? "",
					["description"] = description ?? "",
					["ownerName"] = ownerName,
					["now"] = now
				});
			await ReplacePlotObjectEdgeAsync(newId, ownerDbref);
			var created = await GetPlotAsync($"scene_plot:{newId}");
			return created.IsT0
				? created.AsT0
				: new ScenePlot($"scene_plot:{newId}", title ?? "", description ?? "", DbRefToString(ownerDbref), ownerName, now, now);
		}

		var plotKey = PlotKey(plotId);
		await _accessor.ExecuteAsync(
			"UPDATE $id MERGE { title: $title, description: $description, ownerName: $ownerName, updatedAt: $now }",
			new Dictionary<string, object?>
			{
				["id"] = Rid(plotKey),
				["title"] = title ?? "",
				["description"] = description ?? "",
				["ownerName"] = ownerName,
				["now"] = now
			});
		await ReplacePlotObjectEdgeAsync(plotKey.Split(':')[1], ownerDbref);

		var updated = await GetPlotAsync(plotId);
		return updated.IsT0
			? updated.AsT0
			: new ScenePlot(plotKey, title ?? "", description ?? "", DbRefToString(ownerDbref), ownerName, now, now);
	}

	public async Task<OneOf<ScenePlot, NotFound>> GetPlotAsync(string plotId)
	{
		var key = PlotKey(plotId);
		var response = await _accessor.ExecuteAsync($"SELECT {ScenePlotFields} FROM $id",
			new Dictionary<string, object?> { ["id"] = Rid(key) });		var rows = response.GetValue<List<ScenePlotDbRecord>>(0);
		if (rows is null or { Count: 0 })
			return new NotFound();

		var rec = rows[0];
		var plotKey = NormalizePlotId(rec.Id);
		var (ownerDbref, _) = await ResolvePlotOwnerEdgeAsync(plotKey);
		return new ScenePlot(plotKey, rec.title, rec.description, ownerDbref, rec.ownerName, rec.createdAt, rec.updatedAt);
	}

	public async Task<OneOf<OkNone, NotFound>> LinkSceneToPlotAsync(string plotId, string sceneId)
	{
		var plot = await GetPlotAsync(plotId);
		if (plot.IsT1)
			return new NotFound();
		var scene = await GetSceneAsync(sceneId);
		if (scene.IsT1)
			return new NotFound();

		var plotKey = PlotKey(plotId).Split(':')[1];
		var sceneKey = SceneKey(sceneId).Split(':')[1];
		// Idempotent: clear any existing edge first.
		await _accessor.ExecuteAsync(
			"DELETE scene_plot_includes WHERE in = scene_plot:⟨$pid⟩ AND out = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["pid"] = plotKey, ["sid"] = sceneKey });
		await _accessor.ExecuteAsync(
			"RELATE scene_plot:⟨$pid⟩->scene_plot_includes->scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["pid"] = plotKey, ["sid"] = sceneKey });

		return new OkNone();
	}

	public async Task<OneOf<OkNone, NotFound>> UnlinkSceneFromPlotAsync(string plotId, string sceneId)
	{
		var plot = await GetPlotAsync(plotId);
		if (plot.IsT1)
			return new NotFound();
		var scene = await GetSceneAsync(sceneId);
		if (scene.IsT1)
			return new NotFound();

		var plotKey = PlotKey(plotId).Split(':')[1];
		var sceneKey = SceneKey(sceneId).Split(':')[1];
		await _accessor.ExecuteAsync(
			"DELETE scene_plot_includes WHERE in = scene_plot:⟨$pid⟩ AND out = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["pid"] = plotKey, ["sid"] = sceneKey });

		return new OkNone();
	}

	public async Task<OneOf<IReadOnlyList<string>, NotFound>> GetTagsAsync(string sceneId)
	{
		var posesResult = await GetPosesAsync(sceneId);
		if (posesResult.IsT1)
			return new NotFound();

		var distinct = posesResult.AsT0
			.Where(p => !p.IsDeleted)
			.SelectMany(p => p.Tags)
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Distinct(StringComparer.Ordinal)
			.ToList();
		return OneOf<IReadOnlyList<string>, NotFound>.FromT0(distinct);
	}

	public async Task<OneOf<IReadOnlyList<string>, NotFound>> GetCastAsync(string sceneId)
	{
		var posesResult = await GetPosesAsync(sceneId);
		if (posesResult.IsT1)
			return new NotFound();

		var distinct = posesResult.AsT0
			.Where(p => !p.IsDeleted)
			.Select(p => string.IsNullOrEmpty(p.ShowAsName) ? p.AuthorName : p.ShowAsName)
			.Where(n => !string.IsNullOrWhiteSpace(n))
			.Distinct(StringComparer.Ordinal)
			.ToList();
		return OneOf<IReadOnlyList<string>, NotFound>.FromT0(distinct);
	}

	#endregion

	#region SceneModel Internals

	internal class SceneMemberEdgeRecord
	{
		public string role { get; set; } = "";
		public string showAs { get; set; } = "";
		public bool isCurrent { get; set; }
		public long grantedAt { get; set; }
		public string memberName { get; set; } = "";
		public int? memberKey { get; set; }
	}

	internal class SceneKeyRow
	{
		public int? key { get; set; }
	}

	// Instance (not static): NormalizeId is now an instance call on the host-shared accessor.
	private string SceneKey(string id) => _accessor.NormalizeId(id, "scene");
	private string PoseKey(string id) => _accessor.NormalizeId(id, "scene_pose");
	private string PlotKey(string id) => _accessor.NormalizeId(id, "scene_plot");
	private string EditKeyToId(string editKeyOrId) => _accessor.NormalizeId(editKeyOrId, "scene_pose_edit");

	// Atomic 1-based id counters (counter:scene_id / counter:pose_id, seeded by the migration). The
	// single-record UPDATE is atomic. `seq` (not `value`) because RETURN value collides with the VALUE keyword.
	private ValueTask<int> GetNextSceneIdAsync(CancellationToken ct = default) => NextCounterAsync("scene_id", ct);

	private ValueTask<int> GetNextPoseIdAsync(CancellationToken ct = default) => NextCounterAsync("pose_id", ct);

	private async ValueTask<int> NextCounterAsync(string name, CancellationToken ct)
	{
		var response = await _accessor.ExecuteAsync(
			$"UPDATE counter:{name} SET seq = seq + 1 RETURN seq",
			new Dictionary<string, object?>(), ct);
		return response.GetValue<List<CounterRecord>>(0)?.FirstOrDefault()?.seq ?? 1;
	}

	private record CounterRecord
	{
		public int seq { get; set; }
	}

	// Builds a record-id param that forces the STRING id form (table:⟨id⟩). ExecuteAsync inlines a plain
	// StringRecordId as the bare ref `table:id`, which SurrealDB binds as a NUMBER for numeric-looking ids
	// (e.g. counter id "1" -> table:1) — missing the string-id record that CREATE made via `table:⟨$id⟩`.
	// Wrapping the id in ⟨⟩ keeps it a string and round-trips for both numeric counters and GUIDs.
	private static StringRecordId Rid(string normalizedKey)
	{
		var i = normalizedKey.IndexOf(':');
		return i < 0
			? new StringRecordId(normalizedKey)
			: new StringRecordId($"{normalizedKey[..i]}:⟨{normalizedKey[(i + 1)..]}⟩");
	}

	private static string NormalizeSceneId(RecordId? id) => NormalizeRecordId(id, "scene");
	private static string NormalizePoseId(RecordId? id) => NormalizeRecordId(id, "scene_pose");
	private static string NormalizePlotId(RecordId? id) => NormalizeRecordId(id, "scene_plot");
	private static string NormalizeEditId(RecordId? id) => NormalizeRecordId(id, "scene_pose_edit");

	private static string NormalizeRecordId(RecordId? id, string table)
	{
		ArgumentNullException.ThrowIfNull(id);
		if (id.TryDeserializeId<string>(out var stringId))
			return $"{table}:{stringId}";
		if (id.TryDeserializeId<long>(out var longId))
			return $"{table}:{longId}";
		if (id.TryDeserializeId<int>(out var intId))
			return $"{table}:{intId}";
		throw new InvalidOperationException($"Unsupported SurrealDB {table} record ID type for table '{id.Table}'.");
	}

	/// <summary>Parses a dbref string (e.g. "#5") to its numeric object key, or null if blank/invalid.</summary>
	private static int? DbRefToKey(string? dbref)
	{
		if (string.IsNullOrWhiteSpace(dbref))
			return null;
		return DBRef.TryParse(dbref, out var parsed) && parsed.HasValue ? parsed.Value.Number : null;
	}

	private static string? DbRefToString(string? dbref)
	{
		var key = DbRefToKey(dbref);
		return key is null ? null : $"#{key.Value}";
	}

	/// <summary>Reads the live object's name for a dbref (snapshot capture). Null if missing.</summary>
	private async Task<string?> ResolveObjectNameAsync(string? dbref)
	{
		var key = DbRefToKey(dbref);
		if (key is null)
			return null;
		var response = await _accessor.ExecuteAsync("SELECT name FROM object:$key",
			new Dictionary<string, object?> { ["key"] = key.Value });
		var rows = response.GetValue<List<SceneObjectNameRecord>>(0);
		return rows is { Count: > 0 } ? rows[0].name : null;
	}

	/// <summary>RELATE scene -> object:&lt;key&gt; on the given edge table.</summary>
	private async Task RelateSceneToObjectAsync(string edge, string sceneId, string? objectDbref)
	{
		var key = DbRefToKey(objectDbref);
		if (key is null)
			return;
		await _accessor.ExecuteAsync(
			$"RELATE scene:⟨$sid⟩->{edge}->object:$ok",
			new Dictionary<string, object?> { ["sid"] = sceneId, ["ok"] = key.Value });
	}

	private async Task ReplaceSceneObjectEdgeAsync(string edge, string sceneKey, string? objectDbref)
	{
		var sid = sceneKey.Split(':')[1];
		await _accessor.ExecuteAsync($"DELETE {edge} WHERE in = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["sid"] = sid });
		await RelateSceneToObjectAsync(edge, sid, objectDbref);
	}

	/// <summary>Resolves the live dbref + name reached via a scene -&gt; object edge. Both null if the edge/object is gone.</summary>
	private async Task<(string? Dbref, string? Name)> ResolveSceneObjectEdgeAsync(string edge, string sceneKey)
	{
		var sid = sceneKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			$"SELECT out.key AS key FROM {edge} WHERE in = scene:⟨$sid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["sid"] = sid });
		var rows = response.GetValue<List<SceneKeyRow>>(0);
		if (rows is null or { Count: 0 } || rows[0].key is null)
			return (null, null);
		var key = rows[0].key!.Value;
		return ($"#{key}", null);
	}

	private async Task RelatePoseToObjectAsync(string edge, string poseId, string? objectDbref)
	{
		var key = DbRefToKey(objectDbref);
		if (key is null)
			return;
		await _accessor.ExecuteAsync(
			$"RELATE scene_pose:⟨$pid⟩->{edge}->object:$ok",
			new Dictionary<string, object?> { ["pid"] = poseId, ["ok"] = key.Value });
	}

	private async Task ReplacePoseObjectEdgeAsync(string edge, string poseKey, string? objectDbref)
	{
		var pid = poseKey.Split(':')[1];
		await _accessor.ExecuteAsync($"DELETE {edge} WHERE in = scene_pose:⟨$pid⟩",
			new Dictionary<string, object?> { ["pid"] = pid });
		await RelatePoseToObjectAsync(edge, pid, objectDbref);
	}

	private async Task<(string? Dbref, string? Name)> ResolvePoseObjectEdgeAsync(string edge, string poseKey)
	{
		var pid = poseKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			$"SELECT out.key AS key FROM {edge} WHERE in = scene_pose:⟨$pid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["pid"] = pid });
		var rows = response.GetValue<List<SceneKeyRow>>(0);
		if (rows is null or { Count: 0 } || rows[0].key is null)
			return (null, null);
		return ($"#{rows[0].key!.Value}", null);
	}

	private async Task RelateEditToObjectAsync(string edge, string editId, string? objectDbref)
	{
		var key = DbRefToKey(objectDbref);
		if (key is null)
			return;
		await _accessor.ExecuteAsync(
			$"RELATE scene_pose_edit:⟨$eid⟩->{edge}->object:$ok",
			new Dictionary<string, object?> { ["eid"] = editId, ["ok"] = key.Value });
	}

	private async Task<(string? Dbref, string? Name)> ResolveEditObjectEdgeAsync(string edge, string editKey)
	{
		var eid = editKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			$"SELECT out.key AS key FROM {edge} WHERE in = scene_pose_edit:⟨$eid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["eid"] = eid });
		var rows = response.GetValue<List<SceneKeyRow>>(0);
		if (rows is null or { Count: 0 } || rows[0].key is null)
			return (null, null);
		return ($"#{rows[0].key!.Value}", null);
	}

	private async Task ReplacePlotObjectEdgeAsync(string plotIdSegment, string? objectDbref)
	{
		await _accessor.ExecuteAsync("DELETE scene_plot_owner WHERE in = scene_plot:⟨$pid⟩",
			new Dictionary<string, object?> { ["pid"] = plotIdSegment });
		var key = DbRefToKey(objectDbref);
		if (key is null)
			return;
		await _accessor.ExecuteAsync(
			"RELATE scene_plot:⟨$pid⟩->scene_plot_owner->object:$ok",
			new Dictionary<string, object?> { ["pid"] = plotIdSegment, ["ok"] = key.Value });
	}

	private async Task<(string? Dbref, string? Name)> ResolvePlotOwnerEdgeAsync(string plotKey)
	{
		var pid = plotKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			"SELECT out.key AS key FROM scene_plot_owner WHERE in = scene_plot:⟨$pid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["pid"] = pid });
		var rows = response.GetValue<List<SceneKeyRow>>(0);
		if (rows is null or { Count: 0 } || rows[0].key is null)
			return (null, null);
		return ($"#{rows[0].key!.Value}", null);
	}

	private async Task RelatePoseToSceneAsync(string poseId, string sceneKey)
	{
		await _accessor.ExecuteAsync(
			"RELATE scene_pose:⟨$pid⟩->scene_pose_in_scene->scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["pid"] = poseId, ["sid"] = sceneKey.Split(':')[1] });
	}

	private async Task<string?> ResolvePoseSceneKeyAsync(string poseKey)
	{
		var pid = poseKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			"SELECT VALUE meta::id(out) FROM scene_pose_in_scene WHERE in = scene_pose:⟨$pid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["pid"] = pid });
		var ids = response.GetValue<List<string>>(0);
		if (ids is null or { Count: 0 })
			return null;
		return $"scene:{ids[0]}";
	}

	/// <summary>Appends a pose to the end of the scene's pose_next chain (off last_pose).</summary>
	private async Task AppendPoseToChainAsync(string sceneKey, string poseId)
	{
		var sid = sceneKey.Split(':')[1];
		var lastPoseKey = await ResolveStructuralPointerAsync("scene_last_pose", "scene", sid);

		if (lastPoseKey is null)
		{
			await _accessor.ExecuteAsync(
				"RELATE scene:⟨$sid⟩->scene_first_pose->scene_pose:⟨$pid⟩",
				new Dictionary<string, object?> { ["sid"] = sid, ["pid"] = poseId });
		}
		else
		{
			await _accessor.ExecuteAsync(
				"RELATE scene_pose:⟨$last⟩->scene_pose_next->scene_pose:⟨$pid⟩",
				new Dictionary<string, object?> { ["last"] = lastPoseKey.Split(':')[1], ["pid"] = poseId });
		}

		await _accessor.ExecuteAsync("DELETE scene_last_pose WHERE in = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["sid"] = sid });
		await _accessor.ExecuteAsync(
			"RELATE scene:⟨$sid⟩->scene_last_pose->scene_pose:⟨$pid⟩",
			new Dictionary<string, object?> { ["sid"] = sid, ["pid"] = poseId });
	}

	/// <summary>Walks the pose_next chain from first_pose, returning ordered "scene_pose:&lt;key&gt;" ids.</summary>
	private async Task<List<string>> TraversePoseChainAsync(string sceneKey)
	{
		var sid = sceneKey.Split(':')[1];
		var result = new List<string>();
		var current = await ResolveStructuralPointerAsync("scene_first_pose", "scene", sid);
		var guard = 0;
		while (current is not null && guard++ < 100_000)
		{
			result.Add(current);
			var next = await ResolvePoseNextAsync(current);
			if (next is null || result.Contains(next))
				break;
			current = next;
		}
		return result;
	}

	private async Task<string?> ResolvePoseNextAsync(string poseKey)
	{
		var pid = poseKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			"SELECT VALUE meta::id(out) FROM scene_pose_next WHERE in = scene_pose:⟨$pid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["pid"] = pid });
		var ids = response.GetValue<List<string>>(0);
		if (ids is null or { Count: 0 })
			return null;
		return $"scene_pose:{ids[0]}";
	}

	private async Task<string?> ResolvePosePrevAsync(string sceneKey, string poseKey)
	{
		// The predecessor is whichever pose has pose_next -> poseKey, scoped to this scene's chain.
		var pid = poseKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			"SELECT VALUE meta::id(in) FROM scene_pose_next WHERE out = scene_pose:⟨$pid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["pid"] = pid });
		var ids = response.GetValue<List<string>>(0);
		if (ids is null or { Count: 0 })
			return null;
		return $"scene_pose:{ids[0]}";
	}

	/// <summary>Removes a pose from the chain, stitching prev -&gt; next and fixing first/last pointers.</summary>
	private async Task UnlinkPoseAsync(string sceneKey, string poseKey)
	{
		var sid = sceneKey.Split(':')[1];
		var pid = poseKey.Split(':')[1];
		var prev = await ResolvePosePrevAsync(sceneKey, poseKey);
		var next = await ResolvePoseNextAsync(poseKey);

		await _accessor.ExecuteAsync("DELETE scene_pose_next WHERE in = scene_pose:⟨$pid⟩",
			new Dictionary<string, object?> { ["pid"] = pid });
		await _accessor.ExecuteAsync("DELETE scene_pose_next WHERE out = scene_pose:⟨$pid⟩",
			new Dictionary<string, object?> { ["pid"] = pid });

		if (prev is not null && next is not null)
			await _accessor.ExecuteAsync(
				"RELATE scene_pose:⟨$a⟩->scene_pose_next->scene_pose:⟨$b⟩",
				new Dictionary<string, object?> { ["a"] = prev.Split(':')[1], ["b"] = next.Split(':')[1] });

		if (prev is null)
		{
			await _accessor.ExecuteAsync("DELETE scene_first_pose WHERE in = scene:⟨$sid⟩",
				new Dictionary<string, object?> { ["sid"] = sid });
			if (next is not null)
				await _accessor.ExecuteAsync(
					"RELATE scene:⟨$sid⟩->scene_first_pose->scene_pose:⟨$b⟩",
					new Dictionary<string, object?> { ["sid"] = sid, ["b"] = next.Split(':')[1] });
		}

		if (next is null)
		{
			await _accessor.ExecuteAsync("DELETE scene_last_pose WHERE in = scene:⟨$sid⟩",
				new Dictionary<string, object?> { ["sid"] = sid });
			if (prev is not null)
				await _accessor.ExecuteAsync(
					"RELATE scene:⟨$sid⟩->scene_last_pose->scene_pose:⟨$a⟩",
					new Dictionary<string, object?> { ["sid"] = sid, ["a"] = prev.Split(':')[1] });
		}
	}

	/// <summary>Inserts a (already-unlinked) pose after afterKey, or at the head when afterKey is null.</summary>
	private async Task InsertPoseAfterAsync(string sceneKey, string poseKey, string? afterKey)
	{
		var sid = sceneKey.Split(':')[1];
		var pid = poseKey.Split(':')[1];

		if (afterKey is null)
		{
			// Move to front.
			var oldHead = await ResolveStructuralPointerAsync("scene_first_pose", "scene", sid);
			await _accessor.ExecuteAsync("DELETE scene_first_pose WHERE in = scene:⟨$sid⟩",
				new Dictionary<string, object?> { ["sid"] = sid });
			await _accessor.ExecuteAsync(
				"RELATE scene:⟨$sid⟩->scene_first_pose->scene_pose:⟨$pid⟩",
				new Dictionary<string, object?> { ["sid"] = sid, ["pid"] = pid });
			if (oldHead is not null)
				await _accessor.ExecuteAsync(
					"RELATE scene_pose:⟨$a⟩->scene_pose_next->scene_pose:⟨$b⟩",
					new Dictionary<string, object?> { ["a"] = pid, ["b"] = oldHead.Split(':')[1] });
			else
				// Chain was empty: pose is also the tail.
				await RepointLastPoseAsync(sid, pid);
			return;
		}

		var afterId = afterKey.Split(':')[1];
		var afterNext = await ResolvePoseNextAsync(afterKey);

		// after -> pose
		await _accessor.ExecuteAsync("DELETE scene_pose_next WHERE in = scene_pose:⟨$a⟩",
			new Dictionary<string, object?> { ["a"] = afterId });
		await _accessor.ExecuteAsync(
			"RELATE scene_pose:⟨$a⟩->scene_pose_next->scene_pose:⟨$b⟩",
			new Dictionary<string, object?> { ["a"] = afterId, ["b"] = pid });

		if (afterNext is not null)
			// pose -> afterNext
			await _accessor.ExecuteAsync(
				"RELATE scene_pose:⟨$a⟩->scene_pose_next->scene_pose:⟨$b⟩",
				new Dictionary<string, object?> { ["a"] = pid, ["b"] = afterNext.Split(':')[1] });
		else
			// after was the tail: pose becomes the new tail.
			await RepointLastPoseAsync(sid, pid);
	}

	private async Task RepointLastPoseAsync(string sceneIdSegment, string poseIdSegment)
	{
		await _accessor.ExecuteAsync("DELETE scene_last_pose WHERE in = scene:⟨$sid⟩",
			new Dictionary<string, object?> { ["sid"] = sceneIdSegment });
		await _accessor.ExecuteAsync(
			"RELATE scene:⟨$sid⟩->scene_last_pose->scene_pose:⟨$pid⟩",
			new Dictionary<string, object?> { ["sid"] = sceneIdSegment, ["pid"] = poseIdSegment });
	}

	/// <summary>Resolves a one-hop structural pointer edge (e.g. first_pose/last_pose) to a "table:&lt;key&gt;" id.</summary>
	private async Task<string?> ResolveStructuralPointerAsync(string edge, string fromTable, string fromKeySegment)
	{
		var response = await _accessor.ExecuteAsync(
			$"SELECT VALUE meta::id(out) FROM {edge} WHERE in = {fromTable}:⟨$k⟩ LIMIT 1",
			new Dictionary<string, object?> { ["k"] = fromKeySegment });
		var ids = response.GetValue<List<string>>(0);
		if (ids is null or { Count: 0 })
			return null;
		return $"scene_pose:{ids[0]}";
	}

	private async Task RelatePoseToEditAsync(string edge, string poseId, string editId)
	{
		await _accessor.ExecuteAsync(
			$"RELATE scene_pose:⟨$pid⟩->{edge}->scene_pose_edit:⟨$eid⟩",
			new Dictionary<string, object?> { ["pid"] = poseId, ["eid"] = editId });
	}

	private async Task RelateEditToEditAsync(string edge, string fromEditKey, string toEditId)
	{
		await _accessor.ExecuteAsync(
			$"RELATE scene_pose_edit:⟨$a⟩->{edge}->scene_pose_edit:⟨$b⟩",
			new Dictionary<string, object?> { ["a"] = fromEditKey.Split(':')[1], ["b"] = toEditId });
	}

	/// <summary>Resolves the edit a pose pointer edge (first_edit/current_edit) points at, as "scene_pose_edit:&lt;key&gt;".</summary>
	private async Task<string?> ResolveEditPointerAsync(string edge, string poseKey)
	{
		var pid = poseKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			$"SELECT VALUE meta::id(out) FROM {edge} WHERE in = scene_pose:⟨$pid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["pid"] = pid });
		var ids = response.GetValue<List<string>>(0);
		if (ids is null or { Count: 0 })
			return null;
		return $"scene_pose_edit:{ids[0]}";
	}

	private async Task RepointEditPointerAsync(string edge, string poseKey, string editId)
	{
		var pid = poseKey.Split(':')[1];
		await _accessor.ExecuteAsync($"DELETE {edge} WHERE in = scene_pose:⟨$pid⟩",
			new Dictionary<string, object?> { ["pid"] = pid });
		await _accessor.ExecuteAsync(
			$"RELATE scene_pose:⟨$pid⟩->{edge}->scene_pose_edit:⟨$eid⟩",
			new Dictionary<string, object?> { ["pid"] = pid, ["eid"] = editId });
	}

	private async Task RepointEditPointerByKeyAsync(string edge, string poseKey, string editKey)
		=> await RepointEditPointerAsync(edge, poseKey, editKey.Split(':')[1]);

	/// <summary>Walks first_edit + next_edit, returning ordered "scene_pose_edit:&lt;key&gt;" ids (oldest first).</summary>
	private async Task<List<string>> TraverseEditChainAsync(string poseKey)
	{
		var result = new List<string>();
		var current = await ResolveEditPointerAsync("scene_first_edit", poseKey);
		var guard = 0;
		while (current is not null && guard++ < 100_000)
		{
			result.Add(current);
			var next = await ResolveEditNextAsync(current);
			if (next is null || result.Contains(next))
				break;
			current = next;
		}
		return result;
	}

	private async Task<string?> ResolveEditNextAsync(string editKey)
	{
		var eid = editKey.Split(':')[1];
		var response = await _accessor.ExecuteAsync(
			"SELECT VALUE meta::id(out) FROM scene_next_edit WHERE in = scene_pose_edit:⟨$eid⟩ LIMIT 1",
			new Dictionary<string, object?> { ["eid"] = eid });
		var ids = response.GetValue<List<string>>(0);
		if (ids is null or { Count: 0 })
			return null;
		return $"scene_pose_edit:{ids[0]}";
	}

	/// <summary>Deletes every edit reachable forward of (and including next of) the given edit — truncation on a fresh edit.</summary>
	private async Task TruncateForwardEditsAsync(string fromEditKey)
	{
		var forward = new List<string>();
		var current = await ResolveEditNextAsync(fromEditKey);
		var guard = 0;
		while (current is not null && guard++ < 100_000)
		{
			forward.Add(current);
			current = await ResolveEditNextAsync(current);
		}

		foreach (var editKey in forward)
		{
			var eid = editKey.Split(':')[1];
			await _accessor.ExecuteAsync("DELETE scene_next_edit WHERE in = scene_pose_edit:⟨$eid⟩",
				new Dictionary<string, object?> { ["eid"] = eid });
			await _accessor.ExecuteAsync("DELETE scene_edit_editor WHERE in = scene_pose_edit:⟨$eid⟩",
				new Dictionary<string, object?> { ["eid"] = eid });
			await _accessor.ExecuteAsync("DELETE scene_pose_edit:⟨$eid⟩",
				new Dictionary<string, object?> { ["eid"] = eid });
		}

		// Drop the now-dangling next edge off the surviving tail.
		await _accessor.ExecuteAsync("DELETE scene_next_edit WHERE in = scene_pose_edit:⟨$eid⟩",
			new Dictionary<string, object?> { ["eid"] = fromEditKey.Split(':')[1] });
	}

	private async Task<ScenePoseEdit?> ReadEditAsync(string editKey, string poseKey)
	{
		var response = await _accessor.ExecuteAsync($"SELECT {ScenePoseEditFields} FROM $id",
			new Dictionary<string, object?> { ["id"] = Rid(editKey) });		var rows = response.GetValue<List<ScenePoseEditDbRecord>>(0);
		if (rows is null or { Count: 0 })
			return null;
		var rec = rows[0];
		var idKey = NormalizeEditId(rec.Id);
		var (editorDbref, _) = await ResolveEditObjectEdgeAsync("scene_edit_editor", idKey);
		return new ScenePoseEdit(idKey, poseKey, rec.content, rec.markup, editorDbref, rec.editorName, rec.editedAt);
	}

	private async Task UpdateSceneMetaKeyAsync(string sceneKey, string key, string value, long now)
	{
		var existing = await ReadSceneMetaAsync(sceneKey);
		existing[key] = value;
		await _accessor.ExecuteAsync("UPDATE $id MERGE { meta: $meta, lastActivityAt: $now }",
			new Dictionary<string, object?>
			{
				["id"] = Rid(sceneKey),
				["meta"] = JsonSerializer.Serialize(existing, JsonOptions),
				["now"] = now
			});
	}

	private async Task<Dictionary<string, string>> ReadSceneMetaAsync(string sceneKey)
	{
		var response = await _accessor.ExecuteAsync("SELECT meta FROM $id",
			new Dictionary<string, object?> { ["id"] = Rid(sceneKey) });		var rows = response.GetValue<List<SceneDbRecord>>(0);
		return rows is { Count: > 0 } ? DeserializeMeta(rows[0].meta) : new Dictionary<string, string>();
	}

	private async Task UpdatePoseMetaKeyAsync(string poseKey, string key, string value)
	{
		var response = await _accessor.ExecuteAsync("SELECT meta FROM $id",
			new Dictionary<string, object?> { ["id"] = Rid(poseKey) });		var rows = response.GetValue<List<ScenePoseDbRecord>>(0);
		var meta = rows is { Count: > 0 } ? DeserializeMeta(rows[0].meta) : new Dictionary<string, string>();
		meta[key] = value;
		await _accessor.ExecuteAsync("UPDATE $id MERGE { meta: $meta }",
			new Dictionary<string, object?>
			{
				["id"] = Rid(poseKey),
				["meta"] = JsonSerializer.Serialize(meta, JsonOptions)
			});
	}

	private static Dictionary<string, string> DeserializeMeta(string? json)
	{
		if (string.IsNullOrEmpty(json) || json == "{}")
			return new Dictionary<string, string>();
		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
				?? new Dictionary<string, string>();
		}
		catch
		{
			return new Dictionary<string, string>();
		}
	}

	private static bool ParseBool(string value) =>
		value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

	// Content arrives as raw MString markup; Content (the plain projection) strips ANSI/markup.
	// Falls back to the raw string if the input is not serialized markup.
	private static string StripMarkup(string? content)
	{
		if (string.IsNullOrEmpty(content))
			return "";
		try
		{
			return MModule.plainText(MModule.deserialize(content));
		}
		catch
		{
			return content;
		}
	}

	private async Task<IReadOnlyList<SceneModel>> ProjectScenesAsync(List<SceneDbRecord> rows)
	{
		var result = new List<SceneModel>(rows.Count);
		foreach (var rec in rows)
		{
			var idKey = NormalizeSceneId(rec.Id);
			var (ownerDbref, _) = await ResolveSceneObjectEdgeAsync("scene_owner", idKey);
			var (starterDbref, _) = await ResolveSceneObjectEdgeAsync("scene_starter", idKey);
			var (roomDbref, _) = await ResolveSceneObjectEdgeAsync("scene_in_room", idKey);
			result.Add(ProjectScene(rec, idKey, ownerDbref, starterDbref, roomDbref));
		}
		return result;
	}

	private static SceneModel ProjectScene(SceneDbRecord rec, string idKey,
		string? ownerDbref, string? starterDbref, string? roomDbref) => new(
		Id: idKey,
		Status: rec.status,
		IsPublic: rec.isPublic,
		IsTempRoom: rec.isTempRoom,
		ScheduledFor: rec.scheduledFor,
		StartedAt: rec.startedAt,
		LastActivityAt: rec.lastActivityAt,
		PoseCount: rec.poseCount,
		OwnerDbref: ownerDbref,
		OwnerName: rec.ownerName,
		StarterDbref: starterDbref,
		StarterName: rec.starterName,
		RoomDbref: roomDbref,
		RoomName: rec.roomName,
		Meta: DeserializeMeta(rec.meta));

	private async Task<ScenePose> ProjectPoseAsync(ScenePoseDbRecord rec)
	{
		var idKey = NormalizePoseId(rec.Id);
		var sceneKey = await ResolvePoseSceneKeyAsync(idKey) ?? "";
		var (authorDbref, _) = await ResolvePoseObjectEdgeAsync("scene_pose_author", idKey);
		var (originDbref, _) = await ResolvePoseObjectEdgeAsync("scene_pose_origin", idKey);

		var editChain = await TraverseEditChainAsync(idKey);
		var currentEditKey = await ResolveEditPointerAsync("scene_current_edit", idKey);
		var content = "";
		var markup = "";
		long? lastEditedAt = null;
		string? lastEditorDbref = null;
		string? lastEditorName = null;

		if (currentEditKey is not null)
		{
			var edit = await ReadEditAsync(currentEditKey, idKey);
			if (edit is not null)
			{
				content = edit.Content;
				markup = edit.Markup;
				// "Edited" iff more than one version exists.
				if (editChain.Count > 1)
				{
					lastEditedAt = edit.EditedAt;
					lastEditorDbref = edit.EditorDbref;
					lastEditorName = edit.EditorName;
				}
			}
		}

		return new ScenePose(
			Id: idKey,
			SceneId: sceneKey,
			AuthorDbref: authorDbref,
			AuthorName: rec.authorName,
			ShowAsName: rec.showAsName,
			OriginDbref: originDbref,
			OriginName: rec.originName,
			Source: rec.source,
			Tags: rec.tags ?? [],
			Meta: DeserializeMeta(rec.meta),
			CreatedAt: rec.createdAt,
			IsDeleted: rec.isDeleted,
			Content: content,
			Markup: markup,
			EditCount: Math.Max(1, editChain.Count),
			LastEditedAt: lastEditedAt,
			LastEditorDbref: lastEditorDbref,
			LastEditorName: lastEditorName);
	}

	private static SceneMember ProjectMember(SceneMemberEdgeRecord rec, string sceneKey) => new(
		SceneId: sceneKey,
		MemberDbref: rec.memberKey is null ? null : $"#{rec.memberKey.Value}",
		MemberName: rec.memberName,
		Role: rec.role,
		ShowAs: rec.showAs,
		IsCurrent: rec.isCurrent,
		GrantedAt: rec.grantedAt);

	#endregion
}
