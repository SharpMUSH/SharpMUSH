using Core.Arango.Protocol;
using OneOf;
using OneOf.Types;
using SharpMUSH.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SceneModel = SharpMUSH.Plugins.Scene.Models.Scene;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// ArangoDB storage for the graph-native SceneModel System (<c>graph_sharp_sys_scene</c>), relocated out of the
/// core ArangoDB provider into the SceneModel plugin (Phase 8). Object references are graph edges to the live
/// typed vertex plus a <c>*Name</c> snapshot stored on the scene-side vertex. Pose order is the
/// <c>first_pose</c>/<c>last_pose</c> + <c>pose_next</c> linked list. Pose content is versioned in
/// <c>pose_edits</c> with a <c>current_edit</c> pointer. The provider connection (plus the object-resolution
/// primitive) arrives through the host-shared <see cref="IArangoStorageAccessor"/>; the AQL is verbatim.
/// </summary>
public sealed class ArangoSceneStorage(IArangoStorageAccessor _accessor) : ISceneStorage
{
	#region SceneModel

	/// <summary>Strips an Arango <c>collection/key</c> prefix to the bare key (local copy of the provider helper).</summary>
	private static string ExtractKey(string id) => id.Contains('/') ? id.Split('/')[1] : id;

	/// <summary>
	/// Resolves a dbref string (e.g. <c>#1</c>) to its live typed-vertex id and name
	/// snapshot. Returns <c>(null, "")</c> when the dbref is malformed or the object
	/// no longer exists.
	/// </summary>
	private async Task<(string? VertexId, string Name)> ResolveObjectRefAsync(string dbref)
	{
		if (string.IsNullOrWhiteSpace(dbref) || !DBRef.TryParse(dbref, out var parsed) || parsed is null)
			return (null, string.Empty);

		var node = await _accessor.GetObjectNodeAsync(parsed.Value);
		if (node.IsNone())
			return (null, string.Empty);

		var obj = node.Object();
		return (obj?.Id, obj?.Name ?? string.Empty);
	}

	/// <summary>
	/// Resolves the live dbref for an object-reference edge's target vertex — a
	/// <c>node_objects</c> vertex (key extracted directly) or, defensively, a typed
	/// vertex traversed to its object via the IsObject edge. Returns <c>null</c> when gone.
	/// </summary>
	private async Task<string?> ResolveDbrefFromVertexAsync(string vertexId)
	{
		if (string.IsNullOrWhiteSpace(vertexId))
			return null;

		// Object-reference edges target the node_objects vertex directly (the resolved
		// SharpObject.Id IS the object vertex), so the dbref is simply its key. The
		// IsObject edges run typed -> object, so an OUTBOUND traversal from an object
		// vertex finds nothing — hence the direct key extraction here.
		if (vertexId.StartsWith(DatabaseConstants.Objects + "/", StringComparison.Ordinal))
			return $"#{vertexId.Split('/')[1]}";

		// Defensive fallback: a typed vertex (node_players/node_rooms/…) links OUTBOUND
		// to its object via the IsObject edge.
		var result = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			$"FOR o IN 1..1 OUTBOUND @v GRAPH {DatabaseConstants.GraphObjects} " +
			$"FILTER IS_SAME_COLLECTION(@objects, o) RETURN o._key",
			new Dictionary<string, object>
			{
				{ "v", vertexId },
				{ "objects", DatabaseConstants.Objects }
			});

		var key = result.FirstOrDefault();
		return key is null ? null : $"#{key}";
	}

	/// <summary>
	/// Creates (replacing any existing) an object-reference edge from
	/// <paramref name="fromVertexId"/> to the live vertex resolved from
	/// <paramref name="dbref"/>, returning the captured name snapshot. When the dbref
	/// is empty/unresolvable, any existing edge is removed and an empty snapshot is
	/// returned (roomless / cleared reference).
	/// </summary>
	private async Task<string> SetObjectEdgeAsync(string edgeCollection, string fromVertexId, string dbref)
	{
		// Drop any existing edge of this collection from the source vertex.
		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from REMOVE e IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", edgeCollection },
				{ "from", fromVertexId }
			});

		var (vertexId, name) = await ResolveObjectRefAsync(dbref);
		if (vertexId is null)
			return string.Empty;

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"INSERT { _from: @from, _to: @to } INTO @@e",
			new Dictionary<string, object>
			{
				{ "@e", edgeCollection },
				{ "from", fromVertexId },
				{ "to", vertexId }
			});

		return name;
	}

	/// <summary>Returns the live dbref resolved from the single object-reference edge of a source vertex, or null.</summary>
	private async Task<string?> ReadObjectEdgeDbrefAsync(string edgeCollection, string fromVertexId)
	{
		var result = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", edgeCollection },
				{ "from", fromVertexId }
			});

		var to = result.FirstOrDefault();
		return to is null ? null : await ResolveDbrefFromVertexAsync(to);
	}

	private static long NowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	private static string SceneVertexId(string sceneKey) => $"{SceneArangoConstants.SharpScenes}/{sceneKey}";
	private static string PoseVertexId(string poseKey) => $"{SceneArangoConstants.SharpScenePoses}/{poseKey}";
	private static string EditVertexId(string editKey) => $"{SceneArangoConstants.SharpScenePoseEdits}/{editKey}";
	private static string PlotVertexId(string plotKey) => $"{SceneArangoConstants.SharpScenePlots}/{plotKey}";

	public async Task<SceneModel> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "")
	{
		var now = NowMillis();
		var meta = new Dictionary<string, string>();
		if (!string.IsNullOrEmpty(title))
			meta["title"] = title;

		var doc = new
		{
			Status = "new",
			IsPublic = false,
			IsTempRoom = false,
			ScheduledFor = (long?)null,
			StartedAt = now,
			LastActivityAt = now,
			PoseCount = 0,
			OwnerName = string.Empty,
			StarterName = string.Empty,
			RoomName = string.Empty,
			Meta = meta
		};

		var created = await _accessor.Context.Document.CreateAsync<object, JsonElement>(
			_accessor.Handle, SceneArangoConstants.SharpScenes, doc, returnNew: true);

		var key = created.New.GetProperty("_key").GetString()!;
		var vertexId = SceneVertexId(key);

		var ownerName = await SetObjectEdgeAsync(SceneArangoConstants.SceneOwner, vertexId, ownerDbref);
		// Starter defaults to the owner.
		var starterName = await SetObjectEdgeAsync(SceneArangoConstants.SceneStarter, vertexId, ownerDbref);
		var roomName = string.IsNullOrWhiteSpace(roomDbref)
			? string.Empty
			: await SetObjectEdgeAsync(SceneArangoConstants.SceneInRoom, vertexId, roomDbref);

		await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenes,
			new { _key = key, OwnerName = ownerName, StarterName = starterName, RoomName = roomName },
			mergeObjects: true);

		return (await GetSceneAsync(key)).AsT0;
	}

	public async Task<OneOf<SceneModel, NotFound>> GetSceneAsync(string sceneId)
	{
		var key = ExtractKey(sceneId);
		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			"FOR s IN @@c FILTER s._key == @key RETURN s",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenes },
				{ "key", key }
			});

		if (result.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } elem)
			return new NotFound();

		return await SceneFromJsonAsync(elem);
	}

	public async Task<OneOf<SceneModel, NotFound>> SetSceneMetaAsync(string sceneId, string key, string value)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneKey = ExtractKey(sceneId);
		var vertexId = SceneVertexId(sceneKey);
		var lowered = key.Trim().ToLowerInvariant();
		var now = NowMillis();

		switch (lowered)
		{
			case "status":
				await UpdateSceneFieldsAsync(sceneKey, new { Status = value, LastActivityAt = now });
				break;
			case "public":
				await UpdateSceneFieldsAsync(sceneKey, new { IsPublic = ParseBool(value), LastActivityAt = now });
				break;
			case "istemp":
				await UpdateSceneFieldsAsync(sceneKey, new { IsTempRoom = ParseBool(value), LastActivityAt = now });
				break;
			case "scheduledfor":
				await UpdateSceneFieldsAsync(sceneKey,
					new { ScheduledFor = ParseNullableLong(value), LastActivityAt = now });
				break;
			case "room":
			{
				var name = await SetObjectEdgeAsync(SceneArangoConstants.SceneInRoom, vertexId, value);
				await UpdateSceneFieldsAsync(sceneKey, new { RoomName = name, LastActivityAt = now });
				break;
			}
			case "owner":
			{
				var name = await SetObjectEdgeAsync(SceneArangoConstants.SceneOwner, vertexId, value);
				await UpdateSceneFieldsAsync(sceneKey, new { OwnerName = name, LastActivityAt = now });
				break;
			}
			case "plot":
				// Link this scene into the given plot (value = plot id). Empty unlinks all.
				await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
					"FOR e IN @@e FILTER e._to == @to REMOVE e IN @@e",
					new Dictionary<string, object>
					{
						{ "@e", SceneArangoConstants.ScenePlotIncludes },
						{ "to", vertexId }
					});
				if (!string.IsNullOrWhiteSpace(value))
				{
					var plotResult = await GetPlotAsync(value);
					if (plotResult.IsT0)
						await LinkSceneToPlotAsync(ExtractKey(value), sceneKey);
				}
				await UpdateSceneFieldsAsync(sceneKey, new { LastActivityAt = now });
				break;
			default:
				// Known descriptive keys and any custom key land in Meta.
				await SetSceneMetaKeyAsync(sceneKey, lowered, value);
				await UpdateSceneFieldsAsync(sceneKey, new { LastActivityAt = now });
				break;
		}

		return await GetSceneAsync(sceneKey);
	}

	public async Task<IReadOnlyList<SceneModel>> ListScenesAsync(string filter, string? viewerDbref = null,
		long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50)
	{
		var loweredFilter = (filter ?? string.Empty).Trim().ToLowerInvariant();
		var bindVars = new Dictionary<string, object>
		{
			{ "@c", SceneArangoConstants.SharpScenes },
			{ "count", count }
		};

		string aql;
		switch (loweredFilter)
		{
			case "active":
				aql = "FOR s IN @@c FILTER s.Status == 'active' SORT s.LastActivityAt DESC LIMIT @count RETURN s";
				break;
			case "finished":
				aql = "FOR s IN @@c FILTER s.Status == 'finished' SORT s.LastActivityAt DESC LIMIT @count RETURN s";
				break;
			case "scheduled":
			{
				var filters = "FILTER s.ScheduledFor != null";
				if (fromUtcMillis is not null)
				{
					filters += " FILTER s.ScheduledFor >= @from";
					bindVars["from"] = fromUtcMillis.Value;
				}
				if (toUtcMillis is not null)
				{
					filters += " FILTER s.ScheduledFor <= @to";
					bindVars["to"] = toUtcMillis.Value;
				}
				aql = $"FOR s IN @@c {filters} SORT s.ScheduledFor ASC LIMIT @count RETURN s";
				break;
			}
			case "mine":
			{
				// Scenes the viewer owns, started, or is a member of.
				if (viewerDbref is null)
					return [];
				var (viewerVertex, _) = await ResolveObjectRefAsync(viewerDbref);
				if (viewerVertex is null)
					return [];
				bindVars["viewer"] = viewerVertex;
				bindVars["@owner"] = SceneArangoConstants.SceneOwner;
				bindVars["@starter"] = SceneArangoConstants.SceneStarter;
				bindVars["@member"] = SceneArangoConstants.SceneMember;
				aql =
					"FOR s IN @@c " +
					"LET owned = LENGTH(FOR e IN @@owner FILTER e._from == s._id AND e._to == @viewer RETURN 1) > 0 " +
					"LET started = LENGTH(FOR e IN @@starter FILTER e._from == s._id AND e._to == @viewer RETURN 1) > 0 " +
					"LET joined = LENGTH(FOR e IN @@member FILTER e._to == s._id AND e._from == @viewer RETURN 1) > 0 " +
					"FILTER owned OR started OR joined " +
					"SORT s.LastActivityAt DESC LIMIT @count RETURN s";
				break;
			}
			default: // "recent" and unknown filters
				aql = "FOR s IN @@c SORT s.LastActivityAt DESC LIMIT @count RETURN s";
				break;
		}

		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle, aql, bindVars);

		var scenes = new List<SceneModel>();
		foreach (var elem in result.Where(e => e.ValueKind != JsonValueKind.Undefined))
		{
			var scene = await SceneFromJsonAsync(elem);
			// Visibility: hide non-public scenes from a viewer who is not a member/owner.
			if (!scene.IsPublic && viewerDbref is not null
				&& !await IsVisibleToViewerAsync(scene.Id, viewerDbref))
				continue;
			scenes.Add(scene);
		}

		return scenes.AsReadOnly();
	}

	private async Task<bool> IsVisibleToViewerAsync(string sceneKey, string viewerDbref)
	{
		var (viewerVertex, _) = await ResolveObjectRefAsync(viewerDbref);
		if (viewerVertex is null)
			return false;

		var vertexId = SceneVertexId(sceneKey);
		var result = await _accessor.Context.Query.ExecuteAsync<int>(_accessor.Handle,
			"LET owned = LENGTH(FOR e IN @@owner FILTER e._from == @s AND e._to == @v RETURN 1) " +
			"LET joined = LENGTH(FOR e IN @@member FILTER e._to == @s AND e._from == @v RETURN 1) " +
			"RETURN owned + joined",
			new Dictionary<string, object>
			{
				{ "@owner", SceneArangoConstants.SceneOwner },
				{ "@member", SceneArangoConstants.SceneMember },
				{ "s", vertexId },
				{ "v", viewerVertex }
			});

		return result.FirstOrDefault() > 0;
	}

	public async Task<OneOf<SceneModel, NotFound>> GetActiveSceneInRoomAsync(string roomDbref)
	{
		var (roomVertex, _) = await ResolveObjectRefAsync(roomDbref);
		if (roomVertex is null)
			return new NotFound();

		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			"FOR e IN @@e FILTER e._to == @room " +
			"FOR s IN @@c FILTER s._id == e._from AND s.Status == 'active' " +
			"SORT s.LastActivityAt DESC LIMIT 1 RETURN s",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneInRoom },
				{ "@c", SceneArangoConstants.SharpScenes },
				{ "room", roomVertex }
			});

		if (result.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } elem)
			return new NotFound();

		return await SceneFromJsonAsync(elem);
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> AddPoseAsync(string sceneId, string authorDbref,
		string showAs, string originDbref, string source, IReadOnlyList<string> tags, string content)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneKey = ExtractKey(sceneId);
		var sceneVertex = SceneVertexId(sceneKey);
		var now = NowMillis();

		var (authorVertex, authorName) = await ResolveObjectRefAsync(authorDbref);

		var poseDoc = new
		{
			Source = source ?? string.Empty,
			Tags = (tags ?? []).ToArray(),
			Meta = new Dictionary<string, string>(),
			CreatedAt = now,
			IsDeleted = false,
			AuthorName = authorName,
			ShowAsName = showAs ?? string.Empty,
			OriginName = string.Empty
		};

		var createdPose = await _accessor.Context.Document.CreateAsync<object, JsonElement>(
			_accessor.Handle, SceneArangoConstants.SharpScenePoses, poseDoc, returnNew: true);
		var poseKey = createdPose.New.GetProperty("_key").GetString()!;
		var poseVertex = PoseVertexId(poseKey);

		await InsertEdgeAsync(SceneArangoConstants.ScenePoseInScene, poseVertex, sceneVertex);

		if (authorVertex is not null)
			await InsertEdgeAsync(SceneArangoConstants.ScenePoseAuthor, poseVertex, authorVertex);
		var originName = string.IsNullOrWhiteSpace(originDbref)
			? string.Empty
			: await SetObjectEdgeAsync(SceneArangoConstants.ScenePoseOrigin, poseVertex, originDbref);
		if (!string.IsNullOrEmpty(originName))
			await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
				new { _key = poseKey, OriginName = originName }, mergeObjects: true);

		// First content version + current_edit pointer. The editor of the first
		// version is the author (resolved above; null when the object is gone).
		var editKey = await CreateEditAsync(poseKey, content, authorVertex, authorName, now);
		var editVertex = EditVertexId(editKey);
		await InsertEdgeAsync(SceneArangoConstants.SceneFirstEdit, poseVertex, editVertex);
		await InsertEdgeAsync(SceneArangoConstants.SceneCurrentEdit, poseVertex, editVertex);

		await AppendPoseToChainAsync(sceneVertex, poseVertex);

		await UpdateSceneFieldsAsync(sceneKey, new { PoseCount = sceneResult.AsT0.PoseCount + 1, LastActivityAt = now });

		return (await GetPoseAsync(poseKey)).Match<OneOf<ScenePose, NotFound, Error<string>>>(
			pose => pose,
			_ => new Error<string>("Pose creation failed."));
	}

	public async Task<OneOf<ScenePose, NotFound>> GetPoseAsync(string poseId)
	{
		var key = ExtractKey(poseId);
		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			"FOR p IN @@c FILTER p._key == @key RETURN p",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenePoses },
				{ "key", key }
			});

		if (result.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } elem)
			return new NotFound();

		return await PoseFromJsonAsync(elem);
	}

	public async Task<OneOf<IReadOnlyList<ScenePose>, NotFound>> GetPosesAsync(string sceneId,
		string? authorDbref = null, int? count = null)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneKey = ExtractKey(sceneId);
		var orderedKeys = await GetOrderedPoseKeysAsync(SceneVertexId(sceneKey));

		string? authorVertex = null;
		if (authorDbref is not null)
		{
			(authorVertex, _) = await ResolveObjectRefAsync(authorDbref);
			if (authorVertex is null)
				return OneOf<IReadOnlyList<ScenePose>, NotFound>.FromT0([]);
		}

		var poses = new List<ScenePose>();
		foreach (var poseKey in orderedKeys)
		{
			if (authorVertex is not null && !await PoseHasAuthorAsync(poseKey, authorVertex))
				continue;

			var poseResult = await GetPoseAsync(poseKey);
			if (poseResult.IsT0)
				poses.Add(poseResult.AsT0);
		}

		if (count is not null && count.Value >= 0 && poses.Count > count.Value)
			poses = poses.Skip(poses.Count - count.Value).ToList();

		return OneOf<IReadOnlyList<ScenePose>, NotFound>.FromT0(poses.AsReadOnly());
	}

	public async Task<OneOf<ScenePose, NotFound>> SetPoseMetaAsync(string poseId, string key, string value)
	{
		var poseResult = await GetPoseAsync(poseId);
		if (poseResult.IsT1)
			return new NotFound();

		var poseKey = ExtractKey(poseId);
		var poseVertex = PoseVertexId(poseKey);
		var lowered = key.Trim().ToLowerInvariant();

		switch (lowered)
		{
			case "showas":
				await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
					new { _key = poseKey, ShowAsName = value }, mergeObjects: true);
				break;
			case "authorname":
				await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
					new { _key = poseKey, AuthorName = value }, mergeObjects: true);
				break;
			case "author":
			{
				var name = await SetObjectEdgeAsync(SceneArangoConstants.ScenePoseAuthor, poseVertex, value);
				if (!string.IsNullOrEmpty(name))
					await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
						new { _key = poseKey, AuthorName = name }, mergeObjects: true);
				break;
			}
			case "origin":
			{
				var name = await SetObjectEdgeAsync(SceneArangoConstants.ScenePoseOrigin, poseVertex, value);
				await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
					new { _key = poseKey, OriginName = name }, mergeObjects: true);
				break;
			}
			case "originname":
				await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
					new { _key = poseKey, OriginName = value }, mergeObjects: true);
				break;
			case "source":
				await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
					new { _key = poseKey, Source = value }, mergeObjects: true);
				break;
			case "tags":
			{
				var tagList = value
					.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
					new { _key = poseKey, Tags = tagList }, mergeObjects: true);
				break;
			}
			default:
				await SetPoseMetaKeyAsync(poseKey, lowered, value);
				break;
		}

		return await GetPoseAsync(poseKey);
	}

	public async Task<OneOf<ScenePose, NotFound>> EditPoseAsync(string poseId, string editorDbref, string content)
	{
		var poseResult = await GetPoseAsync(poseId);
		if (poseResult.IsT1)
			return new NotFound();

		var poseKey = ExtractKey(poseId);
		var poseVertex = PoseVertexId(poseKey);
		var now = NowMillis();

		var currentEditKey = await GetCurrentEditKeyAsync(poseVertex);
		if (currentEditKey is null)
			return new NotFound();

		// Truncate any redo-forward versions: drop next_edit edges (and orphaned edits) after current.
		await TruncateForwardEditsAsync(poseVertex, currentEditKey);

		var (editorVertex, editorName) = await ResolveObjectRefAsync(editorDbref);
		var newEditKey = await CreateEditAsync(poseKey, content, editorVertex, editorName, now);
		var newEditVertex = EditVertexId(newEditKey);

		await InsertEdgeAsync(SceneArangoConstants.SceneNextEdit, EditVertexId(currentEditKey), newEditVertex);
		await RepointCurrentEditAsync(poseVertex, newEditVertex);

		await BumpSceneActivityForPoseAsync(poseVertex, now);

		return await GetPoseAsync(poseKey);
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> UndoPoseAsync(string poseId)
	{
		var poseResult = await GetPoseAsync(poseId);
		if (poseResult.IsT1)
			return new NotFound();

		var poseKey = ExtractKey(poseId);
		var poseVertex = PoseVertexId(poseKey);

		var currentEditKey = await GetCurrentEditKeyAsync(poseVertex);
		if (currentEditKey is null)
			return new Error<string>("Pose has no content version.");

		var prev = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._to == @cur LIMIT 1 RETURN e._from",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneNextEdit },
				{ "cur", EditVertexId(currentEditKey) }
			});

		var prevVertex = prev.FirstOrDefault();
		if (prevVertex is null)
			return new Error<string>("Already at the oldest version.");

		await RepointCurrentEditAsync(poseVertex, prevVertex);
		await BumpSceneActivityForPoseAsync(poseVertex, NowMillis());

		return (await GetPoseAsync(poseKey)).Match<OneOf<ScenePose, NotFound, Error<string>>>(
			pose => pose,
			_ => new NotFound());
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> RedoPoseAsync(string poseId)
	{
		var poseResult = await GetPoseAsync(poseId);
		if (poseResult.IsT1)
			return new NotFound();

		var poseKey = ExtractKey(poseId);
		var poseVertex = PoseVertexId(poseKey);

		var currentEditKey = await GetCurrentEditKeyAsync(poseVertex);
		if (currentEditKey is null)
			return new Error<string>("Pose has no content version.");

		var next = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @cur LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneNextEdit },
				{ "cur", EditVertexId(currentEditKey) }
			});

		var nextVertex = next.FirstOrDefault();
		if (nextVertex is null)
			return new Error<string>("Already at the newest version.");

		await RepointCurrentEditAsync(poseVertex, nextVertex);
		await BumpSceneActivityForPoseAsync(poseVertex, NowMillis());

		return (await GetPoseAsync(poseKey)).Match<OneOf<ScenePose, NotFound, Error<string>>>(
			pose => pose,
			_ => new NotFound());
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> MovePoseAsync(string poseId, string afterPoseId)
	{
		var poseResult = await GetPoseAsync(poseId);
		if (poseResult.IsT1)
			return new NotFound();

		var poseKey = ExtractKey(poseId);
		var poseVertex = PoseVertexId(poseKey);
		var sceneVertex = await GetSceneVertexForPoseAsync(poseVertex);
		if (sceneVertex is null)
			return new Error<string>("Pose is not attached to a scene.");

		string? afterVertex = null;
		if (!string.IsNullOrWhiteSpace(afterPoseId))
		{
			var afterResult = await GetPoseAsync(afterPoseId);
			if (afterResult.IsT1)
				return new NotFound();
			afterVertex = PoseVertexId(ExtractKey(afterPoseId));

			var afterScene = await GetSceneVertexForPoseAsync(afterVertex);
			if (afterScene != sceneVertex)
				return new Error<string>("Poses are not in the same scene.");
			if (afterVertex == poseVertex)
				return new Error<string>("Cannot move a pose after itself.");
		}

		await UnlinkPoseFromChainAsync(sceneVertex, poseVertex);
		await LinkPoseAfterAsync(sceneVertex, poseVertex, afterVertex);
		await BumpSceneActivityForPoseAsync(poseVertex, NowMillis());

		return (await GetPoseAsync(poseKey)).Match<OneOf<ScenePose, NotFound, Error<string>>>(
			pose => pose,
			_ => new NotFound());
	}

	public async Task<OneOf<ScenePose, NotFound>> DeletePoseAsync(string poseId)
	{
		var poseResult = await GetPoseAsync(poseId);
		if (poseResult.IsT1)
			return new NotFound();

		var poseKey = ExtractKey(poseId);
		var poseVertex = PoseVertexId(poseKey);
		var now = NowMillis();

		if (!poseResult.AsT0.IsDeleted)
		{
			await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePoses,
				new { _key = poseKey, IsDeleted = true }, mergeObjects: true);

			var sceneVertex = await GetSceneVertexForPoseAsync(poseVertex);
			if (sceneVertex is not null)
			{
				var sceneKey = sceneVertex.Split('/')[1];
				var scene = await GetSceneAsync(sceneKey);
				if (scene.IsT0)
					await UpdateSceneFieldsAsync(sceneKey,
						new { PoseCount = Math.Max(0, scene.AsT0.PoseCount - 1), LastActivityAt = now });
			}
		}

		return await GetPoseAsync(poseKey);
	}

	public async Task<OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>> GetPoseEditsAsync(string poseId)
	{
		var poseKey = ExtractKey(poseId);
		var poseVertex = PoseVertexId(poseKey);

		// Lightweight existence check. We must NOT call GetPoseAsync here: that projects the
		// full pose via PoseFromJsonAsync, which itself calls GetPoseEditsAsync — producing
		// unbounded mutual recursion. A direct document lookup preserves the NotFound contract.
		var exists = await _accessor.Context.Query.ExecuteAsync<int>(_accessor.Handle,
			"FOR p IN @@c FILTER p._key == @key LIMIT 1 RETURN 1",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenePoses },
				{ "key", poseKey }
			});
		if (exists.FirstOrDefault() == 0)
			return new NotFound();

		var firstEdit = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @p LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneFirstEdit },
				{ "p", poseVertex }
			});

		var edits = new List<ScenePoseEdit>();
		var cursor = firstEdit.FirstOrDefault();
		var guard = 0;
		while (cursor is not null && guard++ < 100_000)
		{
			var editKey = cursor.Split('/')[1];
			var editResult = await GetEditAsync(editKey, poseKey);
			if (editResult is not null)
				edits.Add(editResult);

			var nextResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
				"FOR e IN @@e FILTER e._from == @c LIMIT 1 RETURN e._to",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneNextEdit },
					{ "c", cursor }
				});
			cursor = nextResult.FirstOrDefault();
		}

		return OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>.FromT0(edits.AsReadOnly());
	}

	public async Task<OneOf<SceneMember, NotFound>> AddMemberAsync(string sceneId, string playerDbref, string role)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneVertex = SceneVertexId(ExtractKey(sceneId));
		var (playerVertex, playerName) = await ResolveObjectRefAsync(playerDbref);
		if (playerVertex is null)
			return new NotFound();

		var now = NowMillis();

		var existing = await GetMemberEdgeAsync(sceneVertex, playerVertex);
		if (existing is null)
		{
			await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
				"INSERT { _from: @from, _to: @to, role: @role, showAs: '', isCurrent: false, grantedAt: @now, memberName: @name } INTO @@e",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneMember },
					{ "from", playerVertex },
					{ "to", sceneVertex },
					{ "role", role ?? string.Empty },
					{ "now", now },
					{ "name", playerName }
				});
		}
		else
		{
			await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
				"UPDATE @key WITH { role: @role, memberName: @name } IN @@e",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneMember },
					{ "key", existing.Value.Key },
					{ "role", role ?? string.Empty },
					{ "name", playerName }
				});
		}

		return await GetMemberAsync(sceneId, playerDbref);
	}

	public async Task<OneOf<None, NotFound>> RemoveMemberAsync(string sceneId, string playerDbref)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneVertex = SceneVertexId(ExtractKey(sceneId));
		var (playerVertex, _) = await ResolveObjectRefAsync(playerDbref);
		if (playerVertex is null)
			return new None();

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from AND e._to == @to REMOVE e IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneMember },
				{ "from", playerVertex },
				{ "to", sceneVertex }
			});

		return new None();
	}

	public async Task<OneOf<IReadOnlyList<SceneMember>, NotFound>> GetMembersAsync(string sceneId, string? role = null)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneKey = ExtractKey(sceneId);
		var sceneVertex = SceneVertexId(sceneKey);

		var bindVars = new Dictionary<string, object>
		{
			{ "@e", SceneArangoConstants.SceneMember },
			{ "to", sceneVertex }
		};
		var roleFilter = string.Empty;
		if (role is not null)
		{
			roleFilter = " FILTER e.role == @role";
			bindVars["role"] = role;
		}

		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			$"FOR e IN @@e FILTER e._to == @to{roleFilter} RETURN e", bindVars);

		var members = new List<SceneMember>();
		foreach (var elem in result.Where(e => e.ValueKind != JsonValueKind.Undefined))
			members.Add(await MemberFromJsonAsync(elem, sceneKey));

		return OneOf<IReadOnlyList<SceneMember>, NotFound>.FromT0(members.AsReadOnly());
	}

	public async Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneKey = ExtractKey(sceneId);
		var sceneVertex = SceneVertexId(sceneKey);
		var (playerVertex, _) = await ResolveObjectRefAsync(playerDbref);
		if (playerVertex is null)
			return new NotFound();

		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from AND e._to == @to LIMIT 1 RETURN e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneMember },
				{ "from", playerVertex },
				{ "to", sceneVertex }
			});

		if (result.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } elem)
			return new NotFound();

		return await MemberFromJsonAsync(elem, sceneKey);
	}

	public async Task<OneOf<None, NotFound>> SetFocusAsync(string playerDbref, string? sceneId = null)
	{
		var (playerVertex, _) = await ResolveObjectRefAsync(playerDbref);
		if (playerVertex is null)
			return new NotFound();

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from UPDATE e WITH { isCurrent: false } IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneMember },
				{ "from", playerVertex }
			});

		if (string.IsNullOrWhiteSpace(sceneId))
			return new None();

		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneVertex = SceneVertexId(ExtractKey(sceneId));

		var existing = await GetMemberEdgeAsync(sceneVertex, playerVertex);
		if (existing is null)
		{
			var (_, playerName) = await ResolveObjectRefAsync(playerDbref);
			await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
				"INSERT { _from: @from, _to: @to, role: '', showAs: '', isCurrent: true, grantedAt: @now, memberName: @name } INTO @@e",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneMember },
					{ "from", playerVertex },
					{ "to", sceneVertex },
					{ "now", NowMillis() },
					{ "name", playerName }
				});
		}
		else
		{
			await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
				"UPDATE @key WITH { isCurrent: true } IN @@e",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneMember },
					{ "key", existing.Value.Key }
				});
		}

		return new None();
	}

	public async Task<OneOf<SceneModel, NotFound>> GetCurrentSceneAsync(string playerDbref)
	{
		var (playerVertex, _) = await ResolveObjectRefAsync(playerDbref);
		if (playerVertex is null)
			return new NotFound();

		var result = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from AND e.isCurrent == true LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneMember },
				{ "from", playerVertex }
			});

		var sceneVertex = result.FirstOrDefault();
		if (sceneVertex is null)
			return new NotFound();

		return await GetSceneAsync(sceneVertex.Split('/')[1]);
	}

	public async Task<OneOf<SceneMember, NotFound>> SetShowAsAsync(string sceneId, string playerDbref, string showAs)
	{
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var sceneVertex = SceneVertexId(ExtractKey(sceneId));
		var (playerVertex, _) = await ResolveObjectRefAsync(playerDbref);
		if (playerVertex is null)
			return new NotFound();

		var existing = await GetMemberEdgeAsync(sceneVertex, playerVertex);
		if (existing is null)
			return new NotFound();

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"UPDATE @key WITH { showAs: @showAs } IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneMember },
				{ "key", existing.Value.Key },
				{ "showAs", showAs ?? string.Empty }
			});

		return await GetMemberAsync(sceneId, playerDbref);
	}

	public async Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref)
	{
		var now = NowMillis();

		if (string.IsNullOrWhiteSpace(plotId))
		{
			var doc = new
			{
				Title = title ?? string.Empty,
				Description = description ?? string.Empty,
				OwnerName = string.Empty,
				CreatedAt = now,
				UpdatedAt = now
			};

			var created = await _accessor.Context.Document.CreateAsync<object, JsonElement>(
				_accessor.Handle, SceneArangoConstants.SharpScenePlots, doc, returnNew: true);
			var key = created.New.GetProperty("_key").GetString()!;
			var vertexId = PlotVertexId(key);

			var ownerName = await SetObjectEdgeAsync(SceneArangoConstants.ScenePlotOwner, vertexId, ownerDbref);
			await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePlots,
				new { _key = key, OwnerName = ownerName }, mergeObjects: true);

			return (await GetPlotAsync(key)).AsT0;
		}
		else
		{
			var key = ExtractKey(plotId);
			var vertexId = PlotVertexId(key);

			var ownerName = await SetObjectEdgeAsync(SceneArangoConstants.ScenePlotOwner, vertexId, ownerDbref);
			await _accessor.Context.Document.UpdateAsync(_accessor.Handle, SceneArangoConstants.SharpScenePlots,
				new
				{
					_key = key,
					Title = title ?? string.Empty,
					Description = description ?? string.Empty,
					OwnerName = ownerName,
					UpdatedAt = now
				}, mergeObjects: true);

			return (await GetPlotAsync(key)).AsT0;
		}
	}

	public async Task<OneOf<ScenePlot, NotFound>> GetPlotAsync(string plotId)
	{
		var key = ExtractKey(plotId);
		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			"FOR p IN @@c FILTER p._key == @key RETURN p",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenePlots },
				{ "key", key }
			});

		if (result.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } elem)
			return new NotFound();

		return await PlotFromJsonAsync(elem);
	}

	public async Task<OneOf<None, NotFound>> LinkSceneToPlotAsync(string plotId, string sceneId)
	{
		var plotResult = await GetPlotAsync(plotId);
		if (plotResult.IsT1)
			return new NotFound();
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var plotVertex = PlotVertexId(ExtractKey(plotId));
		var sceneVertex = SceneVertexId(ExtractKey(sceneId));

		// Idempotent: only insert when no such edge exists.
		var exists = await _accessor.Context.Query.ExecuteAsync<int>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from AND e._to == @to COLLECT WITH COUNT INTO cnt RETURN cnt",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePlotIncludes },
				{ "from", plotVertex },
				{ "to", sceneVertex }
			});

		if (exists.FirstOrDefault() == 0)
			await InsertEdgeAsync(SceneArangoConstants.ScenePlotIncludes, plotVertex, sceneVertex);

		return new None();
	}

	public async Task<OneOf<None, NotFound>> UnlinkSceneFromPlotAsync(string plotId, string sceneId)
	{
		var plotResult = await GetPlotAsync(plotId);
		if (plotResult.IsT1)
			return new NotFound();
		var sceneResult = await GetSceneAsync(sceneId);
		if (sceneResult.IsT1)
			return new NotFound();

		var plotVertex = PlotVertexId(ExtractKey(plotId));
		var sceneVertex = SceneVertexId(ExtractKey(sceneId));

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from AND e._to == @to REMOVE e IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePlotIncludes },
				{ "from", plotVertex },
				{ "to", sceneVertex }
			});

		return new None();
	}

	public async Task<OneOf<IReadOnlyList<string>, NotFound>> GetTagsAsync(string sceneId)
	{
		var posesResult = await GetPosesAsync(sceneId);
		if (posesResult.IsT1)
			return new NotFound();

		var tags = posesResult.AsT0
			.Where(p => !p.IsDeleted)
			.SelectMany(p => p.Tags)
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Distinct(StringComparer.Ordinal)
			.ToList();

		return OneOf<IReadOnlyList<string>, NotFound>.FromT0(tags.AsReadOnly());
	}

	public async Task<OneOf<IReadOnlyList<string>, NotFound>> GetCastAsync(string sceneId)
	{
		var posesResult = await GetPosesAsync(sceneId);
		if (posesResult.IsT1)
			return new NotFound();

		var cast = posesResult.AsT0
			.Where(p => !p.IsDeleted)
			.Select(p => string.IsNullOrEmpty(p.ShowAsName) ? p.AuthorName : p.ShowAsName)
			.Where(n => !string.IsNullOrWhiteSpace(n))
			.Distinct(StringComparer.Ordinal)
			.ToList();

		return OneOf<IReadOnlyList<string>, NotFound>.FromT0(cast.AsReadOnly());
	}

	#endregion

	#region SceneModel Internals

	private async Task UpdateSceneFieldsAsync(string sceneKey, object fields)
	{
		// Build a patch dictionary (preserving explicit nulls, e.g. clearing ScheduledFor)
		// and apply it via AQL UPDATE so mergeObjects + null semantics are explicit.
		var patch = new Dictionary<string, object?>();
		foreach (var prop in fields.GetType().GetProperties())
			patch[prop.Name] = prop.GetValue(fields);

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"UPDATE @key WITH @patch IN @@c OPTIONS { keepNull: true, mergeObjects: true }",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenes },
				{ "key", sceneKey },
				{ "patch", patch }
			});
	}

	private async Task SetSceneMetaKeyAsync(string sceneKey, string metaKey, string value)
		=> await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"UPDATE @key WITH { Meta: { [@mk]: @mv } } IN @@c OPTIONS { mergeObjects: true }",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenes },
				{ "key", sceneKey },
				{ "mk", metaKey },
				{ "mv", value }
			});

	private async Task SetPoseMetaKeyAsync(string poseKey, string metaKey, string value)
		=> await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"UPDATE @key WITH { Meta: { [@mk]: @mv } } IN @@c OPTIONS { mergeObjects: true }",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenePoses },
				{ "key", poseKey },
				{ "mk", metaKey },
				{ "mv", value }
			});

	private async Task InsertEdgeAsync(string edgeCollection, string from, string to)
		=> await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"INSERT { _from: @from, _to: @to } INTO @@e",
			new Dictionary<string, object>
			{
				{ "@e", edgeCollection },
				{ "from", from },
				{ "to", to }
			});

	private async Task<string> CreateEditAsync(string poseKey, string content, string? editorVertex,
		string editorName, long now)
	{
		var doc = new
		{
			Content = content ?? string.Empty,
			Markup = content ?? string.Empty,
			EditedAt = now,
			EditorName = editorName
		};

		var created = await _accessor.Context.Document.CreateAsync<object, JsonElement>(
			_accessor.Handle, SceneArangoConstants.SharpScenePoseEdits, doc, returnNew: true);
		var editKey = created.New.GetProperty("_key").GetString()!;

		if (editorVertex is not null)
			await InsertEdgeAsync(SceneArangoConstants.SceneEditEditor, EditVertexId(editKey), editorVertex);

		return editKey;
	}

	private async Task<string?> GetCurrentEditKeyAsync(string poseVertex)
	{
		var result = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @p LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneCurrentEdit },
				{ "p", poseVertex }
			});

		var to = result.FirstOrDefault();
		return to?.Split('/')[1];
	}

	private async Task RepointCurrentEditAsync(string poseVertex, string newEditVertex)
		=> await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @p UPDATE e WITH { _to: @to } IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneCurrentEdit },
				{ "p", poseVertex },
				{ "to", newEditVertex }
			});

	private async Task TruncateForwardEditsAsync(string poseVertex, string currentEditKey)
	{
		// Walk next_edit forward from current, deleting subsequent edits and their edges.
		var cursor = EditVertexId(currentEditKey);
		var guard = 0;
		while (guard++ < 100_000)
		{
			var nextResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
				"FOR e IN @@e FILTER e._from == @c LIMIT 1 RETURN e._to",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneNextEdit },
					{ "c", cursor }
				});
			var next = nextResult.FirstOrDefault();
			if (next is null)
				break;

			await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
				"FOR e IN @@e FILTER e._from == @c REMOVE e IN @@e",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneNextEdit },
					{ "c", cursor }
				});

			await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
				"FOR e IN @@e FILTER e._from == @n REMOVE e IN @@e",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneEditEditor },
					{ "n", next }
				});
			await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
				"REMOVE @key IN @@c",
				new Dictionary<string, object>
				{
					{ "@c", SceneArangoConstants.SharpScenePoseEdits },
					{ "key", next.Split('/')[1] }
				});

			cursor = next;
		}
	}

	private async Task BumpSceneActivityForPoseAsync(string poseVertex, long now)
	{
		var sceneVertex = await GetSceneVertexForPoseAsync(poseVertex);
		if (sceneVertex is not null)
			await UpdateSceneFieldsAsync(sceneVertex.Split('/')[1], new { LastActivityAt = now });
	}

	private async Task<string?> GetSceneVertexForPoseAsync(string poseVertex)
	{
		var result = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @p LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePoseInScene },
				{ "p", poseVertex }
			});
		return result.FirstOrDefault();
	}

	private async Task<bool> PoseHasAuthorAsync(string poseKey, string authorVertex)
	{
		var result = await _accessor.Context.Query.ExecuteAsync<int>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @p AND e._to == @a COLLECT WITH COUNT INTO cnt RETURN cnt",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePoseAuthor },
				{ "p", PoseVertexId(poseKey) },
				{ "a", authorVertex }
			});
		return result.FirstOrDefault() > 0;
	}

	private async Task AppendPoseToChainAsync(string sceneVertex, string poseVertex)
	{
		var lastResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @s LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneLastPose },
				{ "s", sceneVertex }
			});
		var last = lastResult.FirstOrDefault();

		if (last is null)
		{
			await InsertEdgeAsync(SceneArangoConstants.SceneFirstPose, sceneVertex, poseVertex);
			await InsertEdgeAsync(SceneArangoConstants.SceneLastPose, sceneVertex, poseVertex);
		}
		else
		{
			await InsertEdgeAsync(SceneArangoConstants.ScenePoseNext, last, poseVertex);
			await RepointSingletonEdgeAsync(SceneArangoConstants.SceneLastPose, sceneVertex, poseVertex);
		}
	}

	private async Task RepointSingletonEdgeAsync(string edgeCollection, string from, string to)
		=> await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from UPDATE e WITH { _to: @to } IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", edgeCollection },
				{ "from", from },
				{ "to", to }
			});

	private async Task<List<string>> GetOrderedPoseKeysAsync(string sceneVertex)
	{
		var firstResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @s LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneFirstPose },
				{ "s", sceneVertex }
			});

		var keys = new List<string>();
		var cursor = firstResult.FirstOrDefault();
		var guard = 0;
		while (cursor is not null && guard++ < 1_000_000)
		{
			keys.Add(cursor.Split('/')[1]);
			var nextResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
				"FOR e IN @@e FILTER e._from == @c LIMIT 1 RETURN e._to",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.ScenePoseNext },
					{ "c", cursor }
				});
			cursor = nextResult.FirstOrDefault();
		}

		return keys;
	}

	private async Task UnlinkPoseFromChainAsync(string sceneVertex, string poseVertex)
	{
		var prevResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._to == @p LIMIT 1 RETURN e._from",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePoseNext },
				{ "p", poseVertex }
			});
		var prev = prevResult.FirstOrDefault();

		var nextResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @p LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePoseNext },
				{ "p", poseVertex }
			});
		var next = nextResult.FirstOrDefault();

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @p OR e._to == @p REMOVE e IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePoseNext },
				{ "p", poseVertex }
			});

		if (prev is not null && next is not null)
			await InsertEdgeAsync(SceneArangoConstants.ScenePoseNext, prev, next);

		if (prev is null)
		{
			if (next is not null)
				await RepointSingletonEdgeAsync(SceneArangoConstants.SceneFirstPose, sceneVertex, next);
			else
				await RemoveSingletonEdgeAsync(SceneArangoConstants.SceneFirstPose, sceneVertex);
		}
		if (next is null)
		{
			if (prev is not null)
				await RepointSingletonEdgeAsync(SceneArangoConstants.SceneLastPose, sceneVertex, prev);
			else
				await RemoveSingletonEdgeAsync(SceneArangoConstants.SceneLastPose, sceneVertex);
		}
	}

	private async Task LinkPoseAfterAsync(string sceneVertex, string poseVertex, string? afterVertex)
	{
		if (afterVertex is null)
		{
			var firstResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
				"FOR e IN @@e FILTER e._from == @s LIMIT 1 RETURN e._to",
				new Dictionary<string, object>
				{
					{ "@e", SceneArangoConstants.SceneFirstPose },
					{ "s", sceneVertex }
				});
			var oldFirst = firstResult.FirstOrDefault();

			if (oldFirst is null)
			{
				await InsertEdgeAsync(SceneArangoConstants.SceneFirstPose, sceneVertex, poseVertex);
				await RepointSingletonEdgeOrInsertAsync(SceneArangoConstants.SceneLastPose, sceneVertex, poseVertex);
			}
			else
			{
				await InsertEdgeAsync(SceneArangoConstants.ScenePoseNext, poseVertex, oldFirst);
				await RepointSingletonEdgeAsync(SceneArangoConstants.SceneFirstPose, sceneVertex, poseVertex);
			}
			return;
		}

		// Insert after afterVertex: afterVertex -> [old successor]; becomes afterVertex -> pose -> successor.
		var succResult = await _accessor.Context.Query.ExecuteAsync<string>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @a LIMIT 1 RETURN e._to",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePoseNext },
				{ "a", afterVertex }
			});
		var successor = succResult.FirstOrDefault();

		await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @a REMOVE e IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.ScenePoseNext },
				{ "a", afterVertex }
			});

		await InsertEdgeAsync(SceneArangoConstants.ScenePoseNext, afterVertex, poseVertex);
		if (successor is not null)
			await InsertEdgeAsync(SceneArangoConstants.ScenePoseNext, poseVertex, successor);
		else
			await RepointSingletonEdgeOrInsertAsync(SceneArangoConstants.SceneLastPose, sceneVertex, poseVertex);
	}

	private async Task RepointSingletonEdgeOrInsertAsync(string edgeCollection, string from, string to)
	{
		var exists = await _accessor.Context.Query.ExecuteAsync<int>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from COLLECT WITH COUNT INTO cnt RETURN cnt",
			new Dictionary<string, object>
			{
				{ "@e", edgeCollection },
				{ "from", from }
			});

		if (exists.FirstOrDefault() == 0)
			await InsertEdgeAsync(edgeCollection, from, to);
		else
			await RepointSingletonEdgeAsync(edgeCollection, from, to);
	}

	private async Task RemoveSingletonEdgeAsync(string edgeCollection, string from)
		=> await _accessor.Context.Query.ExecuteAsync<ArangoVoid>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from REMOVE e IN @@e",
			new Dictionary<string, object>
			{
				{ "@e", edgeCollection },
				{ "from", from }
			});

	private async Task<(string Key, JsonElement Edge)?> GetMemberEdgeAsync(string sceneVertex, string playerVertex)
	{
		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			"FOR e IN @@e FILTER e._from == @from AND e._to == @to LIMIT 1 RETURN e",
			new Dictionary<string, object>
			{
				{ "@e", SceneArangoConstants.SceneMember },
				{ "from", playerVertex },
				{ "to", sceneVertex }
			});

		if (result.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } elem)
			return null;

		return (elem.GetProperty("_key").GetString()!, elem);
	}

	private async Task<SceneModel> SceneFromJsonAsync(JsonElement elem)
	{
		var key = elem.GetProperty("_key").GetString()!;
		var vertexId = SceneVertexId(key);

		var ownerDbref = await ReadObjectEdgeDbrefAsync(SceneArangoConstants.SceneOwner, vertexId);
		var starterDbref = await ReadObjectEdgeDbrefAsync(SceneArangoConstants.SceneStarter, vertexId);
		var roomDbref = await ReadObjectEdgeDbrefAsync(SceneArangoConstants.SceneInRoom, vertexId);

		return new SceneModel(
			Id: key,
			Status: GetString(elem, "Status", "new"),
			IsPublic: GetBool(elem, "IsPublic"),
			IsTempRoom: GetBool(elem, "IsTempRoom"),
			ScheduledFor: GetNullableLong(elem, "ScheduledFor"),
			StartedAt: GetLong(elem, "StartedAt"),
			LastActivityAt: GetLong(elem, "LastActivityAt"),
			PoseCount: (int)GetLong(elem, "PoseCount"),
			OwnerDbref: ownerDbref,
			OwnerName: GetString(elem, "OwnerName", string.Empty),
			StarterDbref: starterDbref,
			StarterName: GetString(elem, "StarterName", string.Empty),
			RoomDbref: roomDbref,
			RoomName: GetString(elem, "RoomName", string.Empty),
			Meta: GetMeta(elem));
	}

	private async Task<ScenePose> PoseFromJsonAsync(JsonElement elem)
	{
		var key = elem.GetProperty("_key").GetString()!;
		var poseVertex = PoseVertexId(key);

		var sceneVertex = await GetSceneVertexForPoseAsync(poseVertex);
		var sceneKey = sceneVertex?.Split('/')[1] ?? string.Empty;

		var authorDbref = await ReadObjectEdgeDbrefAsync(SceneArangoConstants.ScenePoseAuthor, poseVertex);
		var originDbref = await ReadObjectEdgeDbrefAsync(SceneArangoConstants.ScenePoseOrigin, poseVertex);

		var content = string.Empty;
		var markup = string.Empty;
		var editCount = 0;
		long? lastEditedAt = null;
		string? lastEditorDbref = null;
		string? lastEditorName = null;

		var firstEditTime = long.MinValue;
		var editsResult = await GetPoseEditsAsync(key);
		if (editsResult.IsT0)
		{
			var edits = editsResult.AsT0;
			editCount = edits.Count;
			if (edits.Count > 0)
				firstEditTime = edits[0].EditedAt;
		}

		var currentEditKey = await GetCurrentEditKeyAsync(poseVertex);
		if (currentEditKey is not null)
		{
			var edit = await GetEditAsync(currentEditKey, key);
			if (edit is not null)
			{
				content = edit.Content;
				markup = edit.Markup;
				// "edited" only if the current edit differs from the original (first) version.
				if (editCount > 1 && edit.EditedAt > firstEditTime)
				{
					lastEditedAt = edit.EditedAt;
					lastEditorDbref = edit.EditorDbref;
					lastEditorName = edit.EditorName;
				}
			}
		}

		return new ScenePose(
			Id: key,
			SceneId: sceneKey,
			AuthorDbref: authorDbref,
			AuthorName: GetString(elem, "AuthorName", string.Empty),
			ShowAsName: GetString(elem, "ShowAsName", string.Empty),
			OriginDbref: originDbref,
			OriginName: GetString(elem, "OriginName", string.Empty),
			Source: GetString(elem, "Source", string.Empty),
			Tags: GetStringList(elem, "Tags"),
			Meta: GetMeta(elem),
			CreatedAt: GetLong(elem, "CreatedAt"),
			IsDeleted: GetBool(elem, "IsDeleted"),
			Content: content,
			Markup: markup,
			EditCount: editCount,
			LastEditedAt: lastEditedAt,
			LastEditorDbref: lastEditorDbref,
			LastEditorName: lastEditorName);
	}

	private async Task<ScenePoseEdit?> GetEditAsync(string editKey, string poseKey)
	{
		var result = await _accessor.Context.Query.ExecuteAsync<JsonElement>(_accessor.Handle,
			"FOR e IN @@c FILTER e._key == @key RETURN e",
			new Dictionary<string, object>
			{
				{ "@c", SceneArangoConstants.SharpScenePoseEdits },
				{ "key", editKey }
			});

		if (result.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } elem)
			return null;

		var editorDbref = await ReadObjectEdgeDbrefAsync(SceneArangoConstants.SceneEditEditor, EditVertexId(editKey));

		return new ScenePoseEdit(
			Id: editKey,
			PoseId: poseKey,
			Content: GetString(elem, "Content", string.Empty),
			Markup: GetString(elem, "Markup", string.Empty),
			EditorDbref: editorDbref,
			EditorName: GetString(elem, "EditorName", string.Empty),
			EditedAt: GetLong(elem, "EditedAt"));
	}

	private async Task<ScenePlot> PlotFromJsonAsync(JsonElement elem)
	{
		var key = elem.GetProperty("_key").GetString()!;
		var vertexId = PlotVertexId(key);

		var ownerDbref = await ReadObjectEdgeDbrefAsync(SceneArangoConstants.ScenePlotOwner, vertexId);

		return new ScenePlot(
			Id: key,
			Title: GetString(elem, "Title", string.Empty),
			Description: GetString(elem, "Description", string.Empty),
			OwnerDbref: ownerDbref,
			OwnerName: GetString(elem, "OwnerName", string.Empty),
			CreatedAt: GetLong(elem, "CreatedAt"),
			UpdatedAt: GetLong(elem, "UpdatedAt"));
	}

	private async Task<SceneMember> MemberFromJsonAsync(JsonElement elem, string sceneKey)
	{
		var fromVertex = elem.GetProperty("_from").GetString()!;
		var memberDbref = await ResolveDbrefFromVertexAsync(fromVertex);

		return new SceneMember(
			SceneId: sceneKey,
			MemberDbref: memberDbref,
			MemberName: GetString(elem, "memberName", string.Empty),
			Role: GetString(elem, "role", string.Empty),
			ShowAs: GetString(elem, "showAs", string.Empty),
			IsCurrent: GetBool(elem, "isCurrent"),
			GrantedAt: GetLong(elem, "grantedAt"));
	}

	private static string GetString(JsonElement elem, string name, string fallback)
		=> elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
			? p.GetString() ?? fallback
			: fallback;

	private static bool GetBool(JsonElement elem, string name)
		=> elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

	private static long GetLong(JsonElement elem, string name)
		=> elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)
			? v
			: 0L;

	private static long? GetNullableLong(JsonElement elem, string name)
		=> elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)
			? v
			: null;

	private static IReadOnlyList<string> GetStringList(JsonElement elem, string name)
		=> elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
			? p.EnumerateArray()
				.Where(t => t.ValueKind == JsonValueKind.String)
				.Select(t => t.GetString()!)
				.ToList()
			: [];

	private static IReadOnlyDictionary<string, string> GetMeta(JsonElement elem)
	{
		if (!elem.TryGetProperty("Meta", out var p) || p.ValueKind != JsonValueKind.Object)
			return new Dictionary<string, string>();

		var meta = new Dictionary<string, string>();
		foreach (var prop in p.EnumerateObject())
			if (prop.Value.ValueKind == JsonValueKind.String)
				meta[prop.Name] = prop.Value.GetString() ?? string.Empty;
		return meta;
	}

	private static bool ParseBool(string value)
		=> value.Trim() is "1" or "true" or "yes" or "on"
			|| (bool.TryParse(value, out var b) && b);

	private static long? ParseNullableLong(string value)
		=> long.TryParse(value, out var v) ? v : null;

	#endregion
}
