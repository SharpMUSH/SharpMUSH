using Neo4j.Driver;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Scene;
using SceneModel = SharpMUSH.Library.Models.Scene.Scene;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// Memgraph (Neo4j Bolt) storage for the graph-native SceneModel System (<c>graph_sharp_sys_scene</c>),
/// relocated out of the core Memgraph provider into the SceneModel plugin (Phase 8). The live Neo4j driver
/// arrives through the host-shared <see cref="IMemgraphStorageAccessor"/>; the Cypher is verbatim.
/// </summary>
/// <remarks>
/// Node labels are PascalCase scene labels, keyed by a generated <c>sceneId</c>/<c>poseId</c>/<c>editId</c>/
/// <c>plotId</c> string. Object references (room/owner/starter/author/origin/editor/
/// plotowner/member) are relationships into the live <c>:Object</c> node (resolved
/// from the passed dbref's key) <b>plus</b> a <c>*Name</c> snapshot property on the
/// scene-side node so a deleted/renamed object still renders. Pose order is the
/// <c>first_pose</c>/<c>last_pose</c> + <c>pose_next</c> linked list. Pose content is
/// versioned in <c>:SharpScenePoseEdit</c> nodes with a <c>current_edit</c> pointer.
/// All timestamps are UTC Unix-millis.
/// </remarks>
public sealed class MemgraphSceneStorage(IMemgraphStorageAccessor _accessor) : ISceneStorage
{
	// Mirror of the provider's JsonOptions (the meta bag is (de)serialized as a JSON string).
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = null,
		WriteIndented = false
	};

	#region SceneModel

	// ── Label / relationship constants (mapped from DatabaseConstants) ──────────
	// Node labels — PascalCased from the node_* collection names.
	private const string SceneLabel = "SharpScene";
	private const string ScenePoseLabel = "SharpScenePose";
	private const string ScenePoseEditLabel = "SharpScenePoseEdit";
	private const string ScenePlotLabel = "SharpScenePlot";

	// Structural relationship types — UPPER_SNAKE from edge_sharp_sys_scene_*.
	private const string RelFirstPose = "SCENE_FIRST_POSE";
	private const string RelLastPose = "SCENE_LAST_POSE";
	private const string RelPoseNext = "SCENE_POSE_NEXT";
	private const string RelPoseInScene = "SCENE_POSE_IN_SCENE";
	private const string RelFirstEdit = "SCENE_FIRST_EDIT";
	private const string RelCurrentEdit = "SCENE_CURRENT_EDIT";
	private const string RelNextEdit = "SCENE_NEXT_EDIT";
	private const string RelPlotIncludes = "SCENE_PLOT_INCLUDES";

	// Object-graph relationship types (into the live :Object node) + Name snapshots.
	private const string RelInRoom = "SCENE_IN_ROOM";
	private const string RelOwner = "SCENE_OWNER";
	private const string RelStarter = "SCENE_STARTER";
	private const string RelAuthor = "SCENE_AUTHOR";
	private const string RelOrigin = "SCENE_ORIGIN";
	private const string RelEditor = "SCENE_EDITOR";
	private const string RelPlotOwner = "SCENE_PLOTOWNER";
	private const string RelMember = "SCENE_MEMBER";

	// Sentinel for an unset nullable long property (Memgraph has no SQL NULL on a typed prop here).
	private const long NoMillis = -1L;

	// SceneModel keys that route to first-class fields/edges rather than the Meta bag.
	private static readonly HashSet<string> KnownSceneKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"status", "public", "scheduledfor", "istemp", "room", "owner", "plot",
		"title", "summary", "icdate", "location", "type", "warning"
	};

	// Pose keys that route to first-class fields/edges rather than the Meta bag.
	private static readonly HashSet<string> KnownPoseKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"showas", "authorname", "author", "origin", "originname", "source", "tags"
	};

	// ── Scenes ──────────────────────────────────────────────────────────────────

	public async Task<SceneModel> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "")
	{
		var sceneId = Guid.NewGuid().ToString("N");
		var now = NowMillis();

		await using var session = _accessor.Driver.AsyncSession();
		await session.ExecuteWriteAsync(async tx =>
		{
			await tx.RunAsync($$"""
				CREATE (s:{{SceneLabel}} {
					sceneId: $sceneId,
					status: $status,
					isPublic: false,
					isTempRoom: false,
					scheduledFor: $noMillis,
					startedAt: $now,
					lastActivityAt: $now,
					poseCount: 0,
					ownerName: '',
					starterName: '',
					roomName: '',
					metaJson: '{}'
				})
				""",
				new { sceneId, status = "new", noMillis = NoMillis, now });

			// Title is opaque metadata — store in Meta.
			if (!string.IsNullOrEmpty(title))
				await SetSceneMetaInTxAsync(tx, sceneId, "title", title);

			// Owner edge + snapshot (starter defaults to the owner).
			await RelateObjectAsync(tx, SceneLabel, "sceneId", sceneId, RelOwner, ownerDbref, "ownerName");
			await RelateObjectAsync(tx, SceneLabel, "sceneId", sceneId, RelStarter, ownerDbref, "starterName");

			// Optional room binding.
			if (!string.IsNullOrEmpty(roomDbref))
				await RelateObjectAsync(tx, SceneLabel, "sceneId", sceneId, RelInRoom, roomDbref, "roomName");
		});

		var read = await GetSceneAsync(sceneId);
		return read.AsT0;
	}

	public async Task<OneOf<SceneModel, NotFound>> GetSceneAsync(string sceneId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var record = await ReadSceneRecordAsync(session, sceneId);
		return record is null ? new NotFound() : MapScene(record);
	}

	public async Task<OneOf<SceneModel, NotFound>> SetSceneMetaAsync(string sceneId, string key, string value)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var exists = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await SceneExistsAsync(tx, sceneId)) return false;

			var k = key.Trim();
			switch (k.ToLowerInvariant())
			{
				case "status":
					await tx.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $id}}) SET s.status = $v, s.lastActivityAt = $now",
						new { id = sceneId, v = value, now = NowMillis() });
					break;
				case "public":
					await tx.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $id}}) SET s.isPublic = $v",
						new { id = sceneId, v = ParseBool(value) });
					break;
				case "istemp":
					await tx.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $id}}) SET s.isTempRoom = $v",
						new { id = sceneId, v = ParseBool(value) });
					break;
				case "scheduledfor":
					await tx.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $id}}) SET s.scheduledFor = $v",
						new { id = sceneId, v = ParseMillis(value) });
					break;
				case "room":
					if (string.IsNullOrWhiteSpace(value))
						await UnrelateObjectAsync(tx, SceneLabel, "sceneId", sceneId, RelInRoom, "roomName");
					else
						await RelateObjectAsync(tx, SceneLabel, "sceneId", sceneId, RelInRoom, value, "roomName");
					break;
				case "owner":
					await RelateObjectAsync(tx, SceneLabel, "sceneId", sceneId, RelOwner, value, "ownerName");
					break;
				case "plot":
					// Link the scene into the named plot.
					await tx.RunAsync($$"""
						MATCH (s:{{SceneLabel}} {sceneId: $id}), (pl:{{ScenePlotLabel}} {plotId: $plot})
						MERGE (pl)-[:{{RelPlotIncludes}}]->(s)
						""",
						new { id = sceneId, plot = value });
					break;
				default:
					// title/summary/icdate/location/type/warning + custom → Meta.
					await SetSceneMetaInTxAsync(tx, sceneId, k, value);
					break;
			}

			return true;
		});

		if (!exists) return new NotFound();
		return await GetSceneAsync(sceneId);
	}

	public async Task<IReadOnlyList<SceneModel>> ListScenesAsync(string filter, string? viewerDbref = null,
		long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var f = (filter ?? "").Trim().ToLowerInvariant();

		string cypher;
		object parameters;

		switch (f)
		{
			case "scheduled":
			{
				var from = fromUtcMillis ?? long.MinValue;
				var to = toUtcMillis ?? long.MaxValue;
				cypher = $$"""
					MATCH (s:{{SceneLabel}})
					WHERE s.scheduledFor <> $noMillis AND s.scheduledFor >= $from AND s.scheduledFor <= $to
					RETURN s ORDER BY s.scheduledFor ASC LIMIT $count
					""";
				parameters = new { noMillis = NoMillis, from, to, count };
				break;
			}
			case "mine":
			{
				var key = ResolveKey(viewerDbref);
				cypher = $$"""
					MATCH (o:Object {key: $key})-[:{{RelMember}}]->(s:{{SceneLabel}})
					RETURN DISTINCT s ORDER BY s.lastActivityAt DESC LIMIT $count
					""";
				parameters = new { key = key ?? int.MinValue, count };
				break;
			}
			case "active":
			{
				cypher = $$"""
					MATCH (s:{{SceneLabel}}) WHERE s.status = 'active'
					RETURN s ORDER BY s.lastActivityAt DESC LIMIT $count
					""";
				parameters = new { count };
				break;
			}
			default: // "recent" and anything else
			{
				cypher = $$"""
					MATCH (s:{{SceneLabel}})
					RETURN s ORDER BY s.lastActivityAt DESC LIMIT $count
					""";
				parameters = new { count };
				break;
			}
		}

		var result = await session.RunAsync(cypher, parameters);
		var records = await result.ToListAsync();

		var scenes = new List<SceneModel>(records.Count);
		foreach (var r in records)
		{
			var sceneId = r["s"].As<INode>()["sceneId"].As<string>();
			var full = await ReadSceneRecordAsync(session, sceneId);
			if (full is not null) scenes.Add(MapScene(full));
		}

		return scenes.AsReadOnly();
	}

	public async Task<OneOf<SceneModel, NotFound>> GetActiveSceneInRoomAsync(string roomDbref)
	{
		var key = ResolveKey(roomDbref);
		if (key is null) return new NotFound();

		await using var session = _accessor.Driver.AsyncSession();
		var result = await session.RunAsync($$"""
			MATCH (s:{{SceneLabel}})-[:{{RelInRoom}}]->(o:Object {key: $key})
			WHERE s.status = 'active'
			RETURN s.sceneId AS sceneId ORDER BY s.lastActivityAt DESC LIMIT 1
			""",
			new { key = key.Value });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return await GetSceneAsync(records[0]["sceneId"].As<string>());
	}

	// ── Poses ─────────────────────────────────────────────────────────────────

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> AddPoseAsync(string sceneId, string authorDbref,
		string showAs, string originDbref, string source, IReadOnlyList<string> tags, string content)
	{
		var poseId = Guid.NewGuid().ToString("N");
		var editId = Guid.NewGuid().ToString("N");
		var now = NowMillis();
		var (plain, markup) = SplitContent(content);
		var tagList = (tags ?? []).ToList();

		await using var session = _accessor.Driver.AsyncSession();
		var outcome = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await SceneExistsAsync(tx, sceneId))
				return (OneOf<bool, NotFound, Error<string>>)new NotFound();

			// Create the pose slot.
			await tx.RunAsync($$"""
				CREATE (p:{{ScenePoseLabel}} {
					poseId: $poseId,
					source: $source,
					tags: $tags,
					metaJson: '{}',
					createdAt: $now,
					isDeleted: false,
					authorName: '',
					showAsName: $showAs,
					originName: ''
				})
				""",
				new { poseId, source = source ?? "", tags = tagList, now, showAs = showAs ?? "" });

			// pose_in_scene back-reference.
			await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId}), (s:{{SceneLabel}} {sceneId: $sceneId})
				MERGE (p)-[:{{RelPoseInScene}}]->(s)
				""",
				new { poseId, sceneId });

			// Append to the pose_next linked list.
			await AppendPoseToChainAsync(tx, sceneId, poseId);

			// Author/origin object edges + snapshots.
			await RelateObjectAsync(tx, ScenePoseLabel, "poseId", poseId, RelAuthor, authorDbref, "authorName");
			if (!string.IsNullOrEmpty(originDbref))
				await RelateObjectAsync(tx, ScenePoseLabel, "poseId", poseId, RelOrigin, originDbref, "originName");

			// First (current) content version.
			await tx.RunAsync($$"""
				CREATE (e:{{ScenePoseEditLabel}} {
					editId: $editId,
					content: $plain,
					markup: $markup,
					editedAt: $now,
					editorName: ''
				})
				""",
				new { editId, plain, markup, now });
			await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId}), (e:{{ScenePoseEditLabel}} {editId: $editId})
				MERGE (p)-[:{{RelFirstEdit}}]->(e)
				MERGE (p)-[:{{RelCurrentEdit}}]->(e)
				""",
				new { poseId, editId });
			// Editor of the first edit is the author.
			await RelateObjectAsync(tx, ScenePoseEditLabel, "editId", editId, RelEditor, authorDbref, "editorName");

			// Denormalized counters / activity.
			await tx.RunAsync($$"""
				MATCH (s:{{SceneLabel}} {sceneId: $sceneId})
				SET s.poseCount = s.poseCount + 1, s.lastActivityAt = $now
				""",
				new { sceneId, now });

			return (OneOf<bool, NotFound, Error<string>>)true;
		});

		if (outcome.IsT1) return new NotFound();
		if (outcome.IsT2) return outcome.AsT2;

		var read = await GetPoseAsync(poseId);
		return read.Match<OneOf<ScenePose, NotFound, Error<string>>>(p => p, nf => nf);
	}

	public async Task<OneOf<ScenePose, NotFound>> GetPoseAsync(string poseId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var record = await ReadPoseRecordAsync(session, poseId);
		return record is null ? new NotFound() : MapPose(record);
	}

	public async Task<OneOf<IReadOnlyList<ScenePose>, NotFound>> GetPosesAsync(string sceneId,
		string? authorDbref = null, int? count = null)
	{
		await using var session = _accessor.Driver.AsyncSession();

		if (!await SceneExistsSessionAsync(session, sceneId))
			return new NotFound();

		// Walk the pose_next chain from first_pose in order.
		var result = await session.RunAsync($$"""
			MATCH (s:{{SceneLabel}} {sceneId: $sceneId})-[:{{RelFirstPose}}]->(head:{{ScenePoseLabel}})
			MATCH path = (head)-[:{{RelPoseNext}}*0..]->(p:{{ScenePoseLabel}})
			RETURN p.poseId AS poseId ORDER BY length(path) ASC
			""",
			new { sceneId });

		var rows = await result.ToListAsync();
		var orderedIds = rows.Select(r => r["poseId"].As<string>()).ToList();

		var authorKey = ResolveKey(authorDbref);

		var poses = new List<ScenePose>(orderedIds.Count);
		foreach (var id in orderedIds)
		{
			var rec = await ReadPoseRecordAsync(session, id);
			if (rec is null) continue;
			var pose = MapPose(rec);
			if (authorKey is not null && pose.AuthorDbref != $"#{authorKey.Value}")
				continue;
			poses.Add(pose);
		}

		IEnumerable<ScenePose> projected = poses;
		if (count is > 0)
			projected = poses.Skip(Math.Max(0, poses.Count - count.Value));

		return OneOf<IReadOnlyList<ScenePose>, NotFound>.FromT0(projected.ToList().AsReadOnly());
	}

	public async Task<OneOf<ScenePose, NotFound>> SetPoseMetaAsync(string poseId, string key, string value)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var exists = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await PoseExistsAsync(tx, poseId)) return false;

			var k = key.Trim();
			switch (k.ToLowerInvariant())
			{
				case "showas":
					await tx.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) SET p.showAsName = $v",
						new { id = poseId, v = value });
					break;
				case "authorname":
					await tx.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) SET p.authorName = $v",
						new { id = poseId, v = value });
					break;
				case "author":
					await RelateObjectAsync(tx, ScenePoseLabel, "poseId", poseId, RelAuthor, value, "authorName");
					break;
				case "origin":
					if (string.IsNullOrWhiteSpace(value))
						await UnrelateObjectAsync(tx, ScenePoseLabel, "poseId", poseId, RelOrigin, "originName");
					else
						await RelateObjectAsync(tx, ScenePoseLabel, "poseId", poseId, RelOrigin, value, "originName");
					break;
				case "originname":
					await tx.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) SET p.originName = $v",
						new { id = poseId, v = value });
					break;
				case "source":
					await tx.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) SET p.source = $v",
						new { id = poseId, v = value });
					break;
				case "tags":
					var tags = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
						.ToList();
					await tx.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) SET p.tags = $v",
						new { id = poseId, v = tags });
					break;
				default:
					await SetPoseMetaInTxAsync(tx, poseId, k, value);
					break;
			}

			return true;
		});

		if (!exists) return new NotFound();
		return await GetPoseAsync(poseId);
	}

	public async Task<OneOf<ScenePose, NotFound>> EditPoseAsync(string poseId, string editorDbref, string content)
	{
		var editId = Guid.NewGuid().ToString("N");
		var now = NowMillis();
		var (plain, markup) = SplitContent(content);

		await using var session = _accessor.Driver.AsyncSession();
		var exists = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await PoseExistsAsync(tx, poseId)) return false;

			// Truncate any redo-forward versions hanging off the current edit.
			await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[:{{RelCurrentEdit}}]->(cur:{{ScenePoseEditLabel}})
				OPTIONAL MATCH (cur)-[:{{RelNextEdit}}*1..]->(fwd:{{ScenePoseEditLabel}})
				DETACH DELETE fwd
				""",
				new { poseId });

			// Create the new edit version and chain it after the current edit.
			await tx.RunAsync($$"""
				CREATE (e:{{ScenePoseEditLabel}} {
					editId: $editId,
					content: $plain,
					markup: $markup,
					editedAt: $now,
					editorName: ''
				})
				""",
				new { editId, plain, markup, now });
			await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[cur:{{RelCurrentEdit}}]->(old:{{ScenePoseEditLabel}})
				MATCH (e:{{ScenePoseEditLabel}} {editId: $editId})
				MERGE (old)-[:{{RelNextEdit}}]->(e)
				DELETE cur
				MERGE (p)-[:{{RelCurrentEdit}}]->(e)
				""",
				new { poseId, editId });

			await RelateObjectAsync(tx, ScenePoseEditLabel, "editId", editId, RelEditor, editorDbref, "editorName");

			await BumpSceneActivityForPoseAsync(tx, poseId, now);
			return true;
		});

		if (!exists) return new NotFound();
		return await GetPoseAsync(poseId);
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> UndoPoseAsync(string poseId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var outcome = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await PoseExistsAsync(tx, poseId))
				return (OneOf<bool, NotFound, Error<string>>)new NotFound();

			// Find the edit immediately preceding the current edit.
			var result = await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[:{{RelCurrentEdit}}]->(cur:{{ScenePoseEditLabel}})
				MATCH (prev:{{ScenePoseEditLabel}})-[:{{RelNextEdit}}]->(cur)
				RETURN prev.editId AS prevId
				""",
				new { poseId });
			var rows = await result.ToListAsync();
			if (rows.Count == 0)
				return (OneOf<bool, NotFound, Error<string>>)new Error<string>("Already at the oldest version.");

			var prevId = rows[0]["prevId"].As<string>();
			await MoveCurrentEditAsync(tx, poseId, prevId);
			await BumpSceneActivityForPoseAsync(tx, poseId, NowMillis());
			return (OneOf<bool, NotFound, Error<string>>)true;
		});

		if (outcome.IsT1) return new NotFound();
		if (outcome.IsT2) return outcome.AsT2;
		var read = await GetPoseAsync(poseId);
		return read.Match<OneOf<ScenePose, NotFound, Error<string>>>(p => p, nf => nf);
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> RedoPoseAsync(string poseId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var outcome = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await PoseExistsAsync(tx, poseId))
				return (OneOf<bool, NotFound, Error<string>>)new NotFound();

			var result = await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[:{{RelCurrentEdit}}]->(cur:{{ScenePoseEditLabel}})
				MATCH (cur)-[:{{RelNextEdit}}]->(next:{{ScenePoseEditLabel}})
				RETURN next.editId AS nextId
				""",
				new { poseId });
			var rows = await result.ToListAsync();
			if (rows.Count == 0)
				return (OneOf<bool, NotFound, Error<string>>)new Error<string>("Already at the newest version.");

			var nextId = rows[0]["nextId"].As<string>();
			await MoveCurrentEditAsync(tx, poseId, nextId);
			await BumpSceneActivityForPoseAsync(tx, poseId, NowMillis());
			return (OneOf<bool, NotFound, Error<string>>)true;
		});

		if (outcome.IsT1) return new NotFound();
		if (outcome.IsT2) return outcome.AsT2;
		var read = await GetPoseAsync(poseId);
		return read.Match<OneOf<ScenePose, NotFound, Error<string>>>(p => p, nf => nf);
	}

	public async Task<OneOf<ScenePose, NotFound, Error<string>>> MovePoseAsync(string poseId, string afterPoseId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var outcome = await session.ExecuteWriteAsync(async tx =>
		{
			// Resolve the moving pose's scene.
			var sceneResult = await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[:{{RelPoseInScene}}]->(s:{{SceneLabel}})
				RETURN s.sceneId AS sceneId
				""",
				new { poseId });
			var sceneRows = await sceneResult.ToListAsync();
			if (sceneRows.Count == 0)
				return (OneOf<bool, NotFound, Error<string>>)new NotFound();
			var sceneId = sceneRows[0]["sceneId"].As<string>();

			if (!string.IsNullOrEmpty(afterPoseId))
			{
				// The target must belong to the same scene.
				var afterResult = await tx.RunAsync($$"""
					MATCH (a:{{ScenePoseLabel}} {poseId: $afterId})-[:{{RelPoseInScene}}]->(s:{{SceneLabel}})
					RETURN s.sceneId AS sceneId
					""",
					new { afterId = afterPoseId });
				var afterRows = await afterResult.ToListAsync();
				if (afterRows.Count == 0)
					return (OneOf<bool, NotFound, Error<string>>)new NotFound();
				if (afterRows[0]["sceneId"].As<string>() != sceneId)
					return (OneOf<bool, NotFound, Error<string>>)
						new Error<string>("Both poses must belong to the same scene.");
				if (afterPoseId == poseId)
					return (OneOf<bool, NotFound, Error<string>>)
						new Error<string>("Cannot move a pose after itself.");
			}

			await UnlinkPoseFromChainAsync(tx, sceneId, poseId);
			await LinkPoseAfterAsync(tx, sceneId, poseId, afterPoseId);
			await tx.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $sceneId}}) SET s.lastActivityAt = $now",
				new { sceneId, now = NowMillis() });

			return (OneOf<bool, NotFound, Error<string>>)true;
		});

		if (outcome.IsT1) return new NotFound();
		if (outcome.IsT2) return outcome.AsT2;
		var read = await GetPoseAsync(poseId);
		return read.Match<OneOf<ScenePose, NotFound, Error<string>>>(p => p, nf => nf);
	}

	public async Task<OneOf<ScenePose, NotFound>> DeletePoseAsync(string poseId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var exists = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await PoseExistsAsync(tx, poseId)) return false;

			await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})
				SET p.isDeleted = true
				""",
				new { poseId });

			// PoseCount tracks non-deleted poses.
			await tx.RunAsync($$"""
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[:{{RelPoseInScene}}]->(s:{{SceneLabel}})
				SET s.poseCount = CASE WHEN s.poseCount > 0 THEN s.poseCount - 1 ELSE 0 END,
				    s.lastActivityAt = $now
				""",
				new { poseId, now = NowMillis() });

			return true;
		});

		if (!exists) return new NotFound();
		return await GetPoseAsync(poseId);
	}

	public async Task<OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>> GetPoseEditsAsync(string poseId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		if (!await PoseExistsSessionAsync(session, poseId))
			return new NotFound();

		// Walk first_edit → next_edit (oldest first).
		var result = await session.RunAsync($$"""
			MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[:{{RelFirstEdit}}]->(head:{{ScenePoseEditLabel}})
			MATCH path = (head)-[:{{RelNextEdit}}*0..]->(e:{{ScenePoseEditLabel}})
			OPTIONAL MATCH (e)-[:{{RelEditor}}]->(o:Object)
			RETURN e, o.key AS editorKey ORDER BY length(path) ASC
			""",
			new { poseId });

		var rows = await result.ToListAsync();
		var edits = rows.Select(r => MapEdit(poseId, r["e"].As<INode>(), AsNullableInt(r["editorKey"])))
			.ToList()
			.AsReadOnly();

		return OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>.FromT0(edits);
	}

	// ── Members / focus ─────────────────────────────────────────────────────────

	public async Task<OneOf<SceneMember, NotFound>> AddMemberAsync(string sceneId, string playerDbref, string role)
	{
		var key = ResolveKey(playerDbref);
		await using var session = _accessor.Driver.AsyncSession();
		var exists = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await SceneExistsAsync(tx, sceneId)) return false;
			if (key is null) return true; // scene exists but player gone — nothing to relate

			var now = NowMillis();
			await tx.RunAsync($$"""
				MATCH (o:Object {key: $key}), (s:{{SceneLabel}} {sceneId: $sceneId})
				MERGE (o)-[m:{{RelMember}}]->(s)
				ON CREATE SET m.role = $role, m.showAs = '', m.isCurrent = false,
				              m.grantedAt = $now, m.memberName = o.name
				ON MATCH SET m.role = $role, m.memberName = o.name
				""",
				new { key = key.Value, sceneId, role = role ?? "", now });
			return true;
		});

		if (!exists) return new NotFound();
		return await GetMemberAsync(sceneId, playerDbref);
	}

	public async Task<OneOf<None, NotFound>> RemoveMemberAsync(string sceneId, string playerDbref)
	{
		var key = ResolveKey(playerDbref);
		await using var session = _accessor.Driver.AsyncSession();
		var exists = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await SceneExistsAsync(tx, sceneId)) return false;
			if (key is null) return true;

			await tx.RunAsync($$"""
				MATCH (o:Object {key: $key})-[m:{{RelMember}}]->(s:{{SceneLabel}} {sceneId: $sceneId})
				DELETE m
				""",
				new { key = key.Value, sceneId });
			return true;
		});

		return exists
			? OneOf<None, NotFound>.FromT0(new None())
			: new NotFound();
	}

	public async Task<OneOf<IReadOnlyList<SceneMember>, NotFound>> GetMembersAsync(string sceneId, string? role = null)
	{
		await using var session = _accessor.Driver.AsyncSession();
		if (!await SceneExistsSessionAsync(session, sceneId))
			return new NotFound();

		var roleFilter = role is null ? "" : "AND m.role = $role";
		var result = await session.RunAsync($$"""
			MATCH (o:Object)-[m:{{RelMember}}]->(s:{{SceneLabel}} {sceneId: $sceneId})
			WHERE 1 = 1 {{roleFilter}}
			RETURN m, o.key AS memberKey ORDER BY m.grantedAt ASC
			""",
			new { sceneId, role = role ?? "" });

		var rows = await result.ToListAsync();
		var members = rows
			.Select(r => MapMember(sceneId, r["m"].As<IRelationship>(), AsNullableInt(r["memberKey"])))
			.ToList()
			.AsReadOnly();

		return OneOf<IReadOnlyList<SceneMember>, NotFound>.FromT0(members);
	}

	public async Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref)
	{
		var key = ResolveKey(playerDbref);
		if (key is null) return new NotFound();

		await using var session = _accessor.Driver.AsyncSession();
		var result = await session.RunAsync($$"""
			MATCH (o:Object {key: $key})-[m:{{RelMember}}]->(s:{{SceneLabel}} {sceneId: $sceneId})
			RETURN m, o.key AS memberKey
			""",
			new { key = key.Value, sceneId });

		var rows = await result.ToListAsync();
		if (rows.Count == 0) return new NotFound();
		return MapMember(sceneId, rows[0]["m"].As<IRelationship>(), AsNullableInt(rows[0]["memberKey"]));
	}

	public async Task<OneOf<None, NotFound>> SetFocusAsync(string playerDbref, string? sceneId = null)
	{
		var key = ResolveKey(playerDbref);
		if (key is null) return new NotFound();

		await using var session = _accessor.Driver.AsyncSession();
		var outcome = await session.ExecuteWriteAsync(async tx =>
		{
			// Clear isCurrent on all the player's member edges first.
			await tx.RunAsync($$"""
				MATCH (o:Object {key: $key})-[m:{{RelMember}}]->(:{{SceneLabel}})
				SET m.isCurrent = false
				""",
				new { key = key.Value });

			if (string.IsNullOrEmpty(sceneId))
				return true; // focus cleared

			if (!await SceneExistsAsync(tx, sceneId))
				return false;

			// Set isCurrent on the target scene's member edge, creating it if needed.
			var now = NowMillis();
			await tx.RunAsync($$"""
				MATCH (o:Object {key: $key}), (s:{{SceneLabel}} {sceneId: $sceneId})
				MERGE (o)-[m:{{RelMember}}]->(s)
				ON CREATE SET m.role = 'participant', m.showAs = '', m.grantedAt = $now, m.memberName = o.name
				SET m.isCurrent = true
				""",
				new { key = key.Value, sceneId, now });
			return true;
		});

		return outcome
			? OneOf<None, NotFound>.FromT0(new None())
			: new NotFound();
	}

	public async Task<OneOf<SceneModel, NotFound>> GetCurrentSceneAsync(string playerDbref)
	{
		var key = ResolveKey(playerDbref);
		if (key is null) return new NotFound();

		await using var session = _accessor.Driver.AsyncSession();
		var result = await session.RunAsync($$"""
			MATCH (o:Object {key: $key})-[m:{{RelMember}} {isCurrent: true}]->(s:{{SceneLabel}})
			RETURN s.sceneId AS sceneId LIMIT 1
			""",
			new { key = key.Value });

		var rows = await result.ToListAsync();
		if (rows.Count == 0) return new NotFound();
		return await GetSceneAsync(rows[0]["sceneId"].As<string>());
	}

	public async Task<OneOf<SceneMember, NotFound>> SetShowAsAsync(string sceneId, string playerDbref, string showAs)
	{
		var key = ResolveKey(playerDbref);
		await using var session = _accessor.Driver.AsyncSession();
		var exists = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await SceneExistsAsync(tx, sceneId)) return false;
			if (key is null) return true;

			await tx.RunAsync($$"""
				MATCH (o:Object {key: $key})-[m:{{RelMember}}]->(s:{{SceneLabel}} {sceneId: $sceneId})
				SET m.showAs = $showAs
				""",
				new { key = key.Value, sceneId, showAs = showAs ?? "" });
			return true;
		});

		if (!exists) return new NotFound();
		return await GetMemberAsync(sceneId, playerDbref);
	}

	// ── Plots ─────────────────────────────────────────────────────────────────

	public async Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref)
	{
		var id = string.IsNullOrEmpty(plotId) ? Guid.NewGuid().ToString("N") : plotId;
		var now = NowMillis();

		await using var session = _accessor.Driver.AsyncSession();
		await session.ExecuteWriteAsync(async tx =>
		{
			await tx.RunAsync($$"""
				MERGE (pl:{{ScenePlotLabel}} {plotId: $id})
				ON CREATE SET pl.title = $title, pl.description = $description,
				              pl.ownerName = '', pl.createdAt = $now, pl.updatedAt = $now
				ON MATCH SET pl.title = $title, pl.description = $description, pl.updatedAt = $now
				""",
				new { id, title = title ?? "", description = description ?? "", now });

			await RelateObjectAsync(tx, ScenePlotLabel, "plotId", id, RelPlotOwner, ownerDbref, "ownerName");
		});

		var read = await GetPlotAsync(id);
		return read.AsT0;
	}

	public async Task<OneOf<ScenePlot, NotFound>> GetPlotAsync(string plotId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var result = await session.RunAsync($$"""
			MATCH (pl:{{ScenePlotLabel}} {plotId: $id})
			OPTIONAL MATCH (pl)-[:{{RelPlotOwner}}]->(o:Object)
			RETURN pl, o.key AS ownerKey
			""",
			new { id = plotId });

		var rows = await result.ToListAsync();
		if (rows.Count == 0) return new NotFound();
		return MapPlot(rows[0]["pl"].As<INode>(), AsNullableInt(rows[0]["ownerKey"]));
	}

	public async Task<OneOf<None, NotFound>> LinkSceneToPlotAsync(string plotId, string sceneId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var ok = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await PlotExistsAsync(tx, plotId)) return false;
			if (!await SceneExistsAsync(tx, sceneId)) return false;

			await tx.RunAsync($$"""
				MATCH (pl:{{ScenePlotLabel}} {plotId: $plotId}), (s:{{SceneLabel}} {sceneId: $sceneId})
				MERGE (pl)-[:{{RelPlotIncludes}}]->(s)
				""",
				new { plotId, sceneId });
			return true;
		});

		return ok
			? OneOf<None, NotFound>.FromT0(new None())
			: new NotFound();
	}

	public async Task<OneOf<None, NotFound>> UnlinkSceneFromPlotAsync(string plotId, string sceneId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		var ok = await session.ExecuteWriteAsync(async tx =>
		{
			if (!await PlotExistsAsync(tx, plotId)) return false;
			if (!await SceneExistsAsync(tx, sceneId)) return false;

			await tx.RunAsync($$"""
				MATCH (pl:{{ScenePlotLabel}} {plotId: $plotId})-[r:{{RelPlotIncludes}}]->(s:{{SceneLabel}} {sceneId: $sceneId})
				DELETE r
				""",
				new { plotId, sceneId });
			return true;
		});

		return ok
			? OneOf<None, NotFound>.FromT0(new None())
			: new NotFound();
	}

	// ── Derived reads ───────────────────────────────────────────────────────────

	public async Task<OneOf<IReadOnlyList<string>, NotFound>> GetTagsAsync(string sceneId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		if (!await SceneExistsSessionAsync(session, sceneId))
			return new NotFound();

		var result = await session.RunAsync($$"""
			MATCH (s:{{SceneLabel}} {sceneId: $sceneId})<-[:{{RelPoseInScene}}]-(p:{{ScenePoseLabel}})
			WHERE p.isDeleted = false
			UNWIND coalesce(p.tags, []) AS tag
			RETURN DISTINCT tag ORDER BY tag ASC
			""",
			new { sceneId });

		var rows = await result.ToListAsync();
		var tags = rows.Select(r => r["tag"].As<string>())
			.Where(t => !string.IsNullOrEmpty(t))
			.ToList()
			.AsReadOnly();

		return OneOf<IReadOnlyList<string>, NotFound>.FromT0(tags);
	}

	public async Task<OneOf<IReadOnlyList<string>, NotFound>> GetCastAsync(string sceneId)
	{
		await using var session = _accessor.Driver.AsyncSession();
		if (!await SceneExistsSessionAsync(session, sceneId))
			return new NotFound();

		// Display persona = ShowAsName, falling back to AuthorName when blank.
		var result = await session.RunAsync($$"""
			MATCH (s:{{SceneLabel}} {sceneId: $sceneId})<-[:{{RelPoseInScene}}]-(p:{{ScenePoseLabel}})
			WHERE p.isDeleted = false
			WITH CASE WHEN p.showAsName <> '' THEN p.showAsName ELSE p.authorName END AS persona
			RETURN DISTINCT persona ORDER BY persona ASC
			""",
			new { sceneId });

		var rows = await result.ToListAsync();
		var cast = rows.Select(r => r["persona"].As<string>())
			.Where(c => !string.IsNullOrEmpty(c))
			.ToList()
			.AsReadOnly();

		return OneOf<IReadOnlyList<string>, NotFound>.FromT0(cast);
	}

	// ── Pose-chain helpers ──────────────────────────────────────────────────────

	private async Task AppendPoseToChainAsync(IAsyncQueryRunner tx, string sceneId, string poseId)
	{
		// If the scene has no first_pose yet, this pose becomes head + tail.
		var headResult = await tx.RunAsync($$"""
			MATCH (s:{{SceneLabel}} {sceneId: $sceneId})
			OPTIONAL MATCH (s)-[:{{RelLastPose}}]->(tail:{{ScenePoseLabel}})
			RETURN tail.poseId AS tailId
			""",
			new { sceneId });
		var rows = await headResult.ToListAsync();
		var tailId = rows.Count > 0 ? AsNullableString(rows[0]["tailId"]) : null;

		if (tailId is null)
		{
			await tx.RunAsync($$"""
				MATCH (s:{{SceneLabel}} {sceneId: $sceneId}), (p:{{ScenePoseLabel}} {poseId: $poseId})
				MERGE (s)-[:{{RelFirstPose}}]->(p)
				MERGE (s)-[:{{RelLastPose}}]->(p)
				""",
				new { sceneId, poseId });
		}
		else
		{
			await tx.RunAsync($$"""
				MATCH (s:{{SceneLabel}} {sceneId: $sceneId})-[old:{{RelLastPose}}]->(tail:{{ScenePoseLabel}} {poseId: $tailId})
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})
				MERGE (tail)-[:{{RelPoseNext}}]->(p)
				DELETE old
				MERGE (s)-[:{{RelLastPose}}]->(p)
				""",
				new { sceneId, tailId, poseId });
		}
	}

	/// <summary>Splices a pose out of the pose_next chain, repairing head/tail pointers.</summary>
	private async Task UnlinkPoseFromChainAsync(IAsyncQueryRunner tx, string sceneId, string poseId)
	{
		// Resolve predecessor and successor (if any).
		var ctx = await tx.RunAsync($$"""
			MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})
			OPTIONAL MATCH (prev:{{ScenePoseLabel}})-[:{{RelPoseNext}}]->(p)
			OPTIONAL MATCH (p)-[:{{RelPoseNext}}]->(next:{{ScenePoseLabel}})
			RETURN prev.poseId AS prevId, next.poseId AS nextId
			""",
			new { poseId });
		var rows = await ctx.ToListAsync();
		var prevId = rows.Count > 0 ? AsNullableString(rows[0]["prevId"]) : null;
		var nextId = rows.Count > 0 ? AsNullableString(rows[0]["nextId"]) : null;

		// Detach the pose's own pose_next links.
		await tx.RunAsync($$"""
			MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})
			OPTIONAL MATCH (prev:{{ScenePoseLabel}})-[r1:{{RelPoseNext}}]->(p)
			OPTIONAL MATCH (p)-[r2:{{RelPoseNext}}]->(next:{{ScenePoseLabel}})
			DELETE r1, r2
			""",
			new { poseId });

		// Stitch predecessor → successor.
		if (prevId is not null && nextId is not null)
		{
			await tx.RunAsync($$"""
				MATCH (prev:{{ScenePoseLabel}} {poseId: $prevId}), (next:{{ScenePoseLabel}} {poseId: $nextId})
				MERGE (prev)-[:{{RelPoseNext}}]->(next)
				""",
				new { prevId, nextId });
		}

		// Repair head pointer if the removed pose was the head.
		if (prevId is null)
		{
			await tx.RunAsync($$"""
				MATCH (s:{{SceneLabel}} {sceneId: $sceneId})-[r:{{RelFirstPose}}]->(p:{{ScenePoseLabel}} {poseId: $poseId})
				DELETE r
				""",
				new { sceneId, poseId });
			if (nextId is not null)
				await tx.RunAsync($$"""
					MATCH (s:{{SceneLabel}} {sceneId: $sceneId}), (n:{{ScenePoseLabel}} {poseId: $nextId})
					MERGE (s)-[:{{RelFirstPose}}]->(n)
					""",
					new { sceneId, nextId });
		}

		// Repair tail pointer if the removed pose was the tail.
		if (nextId is null)
		{
			await tx.RunAsync($$"""
				MATCH (s:{{SceneLabel}} {sceneId: $sceneId})-[r:{{RelLastPose}}]->(p:{{ScenePoseLabel}} {poseId: $poseId})
				DELETE r
				""",
				new { sceneId, poseId });
			if (prevId is not null)
				await tx.RunAsync($$"""
					MATCH (s:{{SceneLabel}} {sceneId: $sceneId}), (pr:{{ScenePoseLabel}} {poseId: $prevId})
					MERGE (s)-[:{{RelLastPose}}]->(pr)
					""",
					new { sceneId, prevId });
		}
	}

	/// <summary>Re-inserts an already-unlinked pose after the given pose (empty = front).</summary>
	private async Task LinkPoseAfterAsync(IAsyncQueryRunner tx, string sceneId, string poseId, string afterPoseId)
	{
		if (string.IsNullOrEmpty(afterPoseId))
		{
			// Move to the front: pose → old head, repoint first_pose.
			var headResult = await tx.RunAsync($$"""
				MATCH (s:{{SceneLabel}} {sceneId: $sceneId})
				OPTIONAL MATCH (s)-[:{{RelFirstPose}}]->(head:{{ScenePoseLabel}})
				RETURN head.poseId AS headId
				""",
				new { sceneId });
			var rows = await headResult.ToListAsync();
			var headId = rows.Count > 0 ? AsNullableString(rows[0]["headId"]) : null;

			if (headId is null)
			{
				await tx.RunAsync($$"""
					MATCH (s:{{SceneLabel}} {sceneId: $sceneId}), (p:{{ScenePoseLabel}} {poseId: $poseId})
					MERGE (s)-[:{{RelFirstPose}}]->(p)
					MERGE (s)-[:{{RelLastPose}}]->(p)
					""",
					new { sceneId, poseId });
			}
			else
			{
				await tx.RunAsync($$"""
					MATCH (s:{{SceneLabel}} {sceneId: $sceneId})-[old:{{RelFirstPose}}]->(head:{{ScenePoseLabel}} {poseId: $headId})
					MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})
					MERGE (p)-[:{{RelPoseNext}}]->(head)
					DELETE old
					MERGE (s)-[:{{RelFirstPose}}]->(p)
					""",
					new { sceneId, headId, poseId });
			}
			return;
		}

		// Insert after a specific pose: after → pose → after.next.
		var afterCtx = await tx.RunAsync($$"""
			MATCH (a:{{ScenePoseLabel}} {poseId: $afterId})
			OPTIONAL MATCH (a)-[:{{RelPoseNext}}]->(next:{{ScenePoseLabel}})
			RETURN next.poseId AS nextId
			""",
			new { afterId = afterPoseId });
		var afterRows = await afterCtx.ToListAsync();
		var nextId = afterRows.Count > 0 ? AsNullableString(afterRows[0]["nextId"]) : null;

		if (nextId is null)
		{
			// afterPose was the tail — append + repoint last_pose.
			await tx.RunAsync($$"""
				MATCH (a:{{ScenePoseLabel}} {poseId: $afterId}), (p:{{ScenePoseLabel}} {poseId: $poseId})
				MERGE (a)-[:{{RelPoseNext}}]->(p)
				""",
				new { afterId = afterPoseId, poseId });
			await tx.RunAsync($$"""
				MATCH (s:{{SceneLabel}} {sceneId: $sceneId})-[old:{{RelLastPose}}]->(:{{ScenePoseLabel}})
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})
				DELETE old
				MERGE (s)-[:{{RelLastPose}}]->(p)
				""",
				new { sceneId, poseId });
		}
		else
		{
			await tx.RunAsync($$"""
				MATCH (a:{{ScenePoseLabel}} {poseId: $afterId})-[old:{{RelPoseNext}}]->(next:{{ScenePoseLabel}} {poseId: $nextId})
				MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})
				DELETE old
				MERGE (a)-[:{{RelPoseNext}}]->(p)
				MERGE (p)-[:{{RelPoseNext}}]->(next)
				""",
				new { afterId = afterPoseId, nextId, poseId });
		}
	}

	private async Task MoveCurrentEditAsync(IAsyncQueryRunner tx, string poseId, string editId)
	{
		await tx.RunAsync($$"""
			MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[cur:{{RelCurrentEdit}}]->(:{{ScenePoseEditLabel}})
			MATCH (e:{{ScenePoseEditLabel}} {editId: $editId})
			DELETE cur
			MERGE (p)-[:{{RelCurrentEdit}}]->(e)
			""",
			new { poseId, editId });
	}

	private async Task BumpSceneActivityForPoseAsync(IAsyncQueryRunner tx, string poseId, long now)
	{
		await tx.RunAsync($$"""
			MATCH (p:{{ScenePoseLabel}} {poseId: $poseId})-[:{{RelPoseInScene}}]->(s:{{SceneLabel}})
			SET s.lastActivityAt = $now
			""",
			new { poseId, now });
	}

	// ── Object-edge + Meta helpers ───────────────────────────────────────────────

	/// <summary>
	/// MERGEs a single relationship of <paramref name="relType"/> from the scene-side
	/// node to the live <c>:Object</c> resolved from <paramref name="dbref"/>, and
	/// snapshots the object's name into <paramref name="snapshotProp"/>. If the dbref
	/// does not resolve, the snapshot is left untouched and no edge is created.
	/// </summary>
	private static async Task RelateObjectAsync(IAsyncQueryRunner tx, string srcLabel, string srcKeyProp,
		string srcKeyValue, string relType, string? dbref, string snapshotProp)
	{
		var key = ResolveKey(dbref);
		if (key is null) return;

		// Replace any existing edge of this type (single-valued reference).
		await tx.RunAsync($$"""
			MATCH (src:{{srcLabel}} {{{srcKeyProp}}: $srcKey})-[r:{{relType}}]->(:Object)
			DELETE r
			""",
			new { srcKey = srcKeyValue });

		await tx.RunAsync($$"""
			MATCH (src:{{srcLabel}} {{{srcKeyProp}}: $srcKey}), (o:Object {key: $objKey})
			MERGE (src)-[:{{relType}}]->(o)
			SET src.{{snapshotProp}} = o.name
			""",
			new { srcKey = srcKeyValue, objKey = key.Value });
	}

	private static async Task UnrelateObjectAsync(IAsyncQueryRunner tx, string srcLabel, string srcKeyProp,
		string srcKeyValue, string relType, string snapshotProp)
	{
		await tx.RunAsync($$"""
			MATCH (src:{{srcLabel}} {{{srcKeyProp}}: $srcKey})-[r:{{relType}}]->(:Object)
			DELETE r
			""",
			new { srcKey = srcKeyValue });
		await tx.RunAsync($"MATCH (src:{srcLabel} {{{srcKeyProp}: $srcKey}}) SET src.{snapshotProp} = ''",
			new { srcKey = srcKeyValue });
	}

	private async Task SetSceneMetaInTxAsync(IAsyncQueryRunner tx, string sceneId, string key, string value)
	{
		var current = await ReadMetaJsonAsync(tx, SceneLabel, "sceneId", sceneId);
		var meta = DeserializeMeta(current);
		meta[key] = value;
		await tx.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $id}}) SET s.metaJson = $json, s.lastActivityAt = $now",
			new { id = sceneId, json = SerializeMeta(meta), now = NowMillis() });
	}

	private async Task SetPoseMetaInTxAsync(IAsyncQueryRunner tx, string poseId, string key, string value)
	{
		var current = await ReadMetaJsonAsync(tx, ScenePoseLabel, "poseId", poseId);
		var meta = DeserializeMeta(current);
		meta[key] = value;
		await tx.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) SET p.metaJson = $json",
			new { id = poseId, json = SerializeMeta(meta) });
	}

	private static async Task<string> ReadMetaJsonAsync(IAsyncQueryRunner tx, string label, string keyProp, string keyVal)
	{
		var result = await tx.RunAsync($"MATCH (n:{label} {{{keyProp}: $id}}) RETURN n.metaJson AS metaJson",
			new { id = keyVal });
		var rows = await result.ToListAsync();
		if (rows.Count == 0) return "{}";
		return AsNullableString(rows[0]["metaJson"]) ?? "{}";
	}

	// ── Existence checks ──────────────────────────────────────────────────────────

	private static async Task<bool> SceneExistsAsync(IAsyncQueryRunner tx, string sceneId)
	{
		var result = await tx.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $id}}) RETURN s.sceneId AS id LIMIT 1",
			new { id = sceneId });
		return (await result.ToListAsync()).Count > 0;
	}

	private static async Task<bool> SceneExistsSessionAsync(IAsyncSession session, string sceneId)
	{
		var result = await session.RunAsync($"MATCH (s:{SceneLabel} {{sceneId: $id}}) RETURN s.sceneId AS id LIMIT 1",
			new { id = sceneId });
		return (await result.ToListAsync()).Count > 0;
	}

	private static async Task<bool> PoseExistsAsync(IAsyncQueryRunner tx, string poseId)
	{
		var result = await tx.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) RETURN p.poseId AS id LIMIT 1",
			new { id = poseId });
		return (await result.ToListAsync()).Count > 0;
	}

	private static async Task<bool> PoseExistsSessionAsync(IAsyncSession session, string poseId)
	{
		var result = await session.RunAsync($"MATCH (p:{ScenePoseLabel} {{poseId: $id}}) RETURN p.poseId AS id LIMIT 1",
			new { id = poseId });
		return (await result.ToListAsync()).Count > 0;
	}

	private static async Task<bool> PlotExistsAsync(IAsyncQueryRunner tx, string plotId)
	{
		var result = await tx.RunAsync($"MATCH (pl:{ScenePlotLabel} {{plotId: $id}}) RETURN pl.plotId AS id LIMIT 1",
			new { id = plotId });
		return (await result.ToListAsync()).Count > 0;
	}

	// ── Read projections (resolve live edges + read snapshots) ─────────────────────

	private async Task<SceneRow?> ReadSceneRecordAsync(IAsyncSession session, string sceneId)
	{
		var result = await session.RunAsync($$"""
			MATCH (s:{{SceneLabel}} {sceneId: $id})
			OPTIONAL MATCH (s)-[:{{RelOwner}}]->(ownerO:Object)
			OPTIONAL MATCH (s)-[:{{RelStarter}}]->(starterO:Object)
			OPTIONAL MATCH (s)-[:{{RelInRoom}}]->(roomO:Object)
			RETURN s, ownerO.key AS ownerKey, starterO.key AS starterKey, roomO.key AS roomKey
			""",
			new { id = sceneId });
		var rows = await result.ToListAsync();
		if (rows.Count == 0) return null;
		var r = rows[0];
		return new SceneRow(
			r["s"].As<INode>(),
			AsNullableInt(r["ownerKey"]),
			AsNullableInt(r["starterKey"]),
			AsNullableInt(r["roomKey"]));
	}

	private async Task<PoseRow?> ReadPoseRecordAsync(IAsyncSession session, string poseId)
	{
		var result = await session.RunAsync($$"""
			MATCH (p:{{ScenePoseLabel}} {poseId: $id})-[:{{RelPoseInScene}}]->(s:{{SceneLabel}})
			MATCH (p)-[:{{RelFirstEdit}}]->(head:{{ScenePoseEditLabel}})
			MATCH editPath = (head)-[:{{RelNextEdit}}*0..]->(:{{ScenePoseEditLabel}})
			OPTIONAL MATCH (p)-[:{{RelAuthor}}]->(authorO:Object)
			OPTIONAL MATCH (p)-[:{{RelOrigin}}]->(originO:Object)
			OPTIONAL MATCH (p)-[:{{RelCurrentEdit}}]->(cur:{{ScenePoseEditLabel}})
			OPTIONAL MATCH (cur)-[:{{RelEditor}}]->(editorO:Object)
			RETURN p, s.sceneId AS sceneId,
			       authorO.key AS authorKey, originO.key AS originKey,
			       cur, editorO.key AS editorKey,
			       max(length(editPath)) AS maxEditIdx
			""",
			new { id = poseId });
		var rows = await result.ToListAsync();
		if (rows.Count == 0) return null;
		var r = rows[0];
		var curNode = AsNullableNode(r["cur"]);
		var editCount = (int)(AsNullableLong(r["maxEditIdx"]) ?? 0) + 1;
		return new PoseRow(
			r["p"].As<INode>(),
			r["sceneId"].As<string>(),
			AsNullableInt(r["authorKey"]),
			AsNullableInt(r["originKey"]),
			curNode,
			AsNullableInt(r["editorKey"]),
			editCount);
	}

	// ── Mappers ────────────────────────────────────────────────────────────────

	private static SceneModel MapScene(SceneRow row)
	{
		var n = row.Node;
		var scheduled = n["scheduledFor"].As<long>();
		return new SceneModel(
			Id: n["sceneId"].As<string>(),
			Status: n["status"].As<string>(),
			IsPublic: n["isPublic"].As<bool>(),
			IsTempRoom: n["isTempRoom"].As<bool>(),
			ScheduledFor: scheduled == NoMillis ? null : scheduled,
			StartedAt: n["startedAt"].As<long>(),
			LastActivityAt: n["lastActivityAt"].As<long>(),
			PoseCount: n["poseCount"].As<int>(),
			OwnerDbref: KeyToDbref(row.OwnerKey),
			OwnerName: n["ownerName"].As<string>(),
			StarterDbref: KeyToDbref(row.StarterKey),
			StarterName: n["starterName"].As<string>(),
			RoomDbref: KeyToDbref(row.RoomKey),
			RoomName: n["roomName"].As<string>(),
			Meta: DeserializeMeta(AsNullableString(n.Properties.GetValueOrDefault("metaJson")) ?? "{}"));
	}

	private static ScenePose MapPose(PoseRow row)
	{
		var n = row.Node;
		var cur = row.CurrentEdit;
		var content = cur is null ? "" : cur["content"].As<string>();
		var markup = cur is null ? "" : cur["markup"].As<string>();
		var lastEditedAt = cur is null ? (long?)null : cur["editedAt"].As<long>();
		var tags = n.Properties.TryGetValue("tags", out var tagsVal) && tagsVal is not null
			? tagsVal.As<List<string>>()
			: [];

		// LastEditor data only meaningful when the pose has been edited (>1 version).
		long? lastEdited = row.EditCount > 1 ? lastEditedAt : null;
		var lastEditorName = row.EditCount > 1 && cur is not null
			? cur["editorName"].As<string>()
			: null;
		var lastEditorDbref = row.EditCount > 1 ? KeyToDbref(row.EditorKey) : null;

		return new ScenePose(
			Id: n["poseId"].As<string>(),
			SceneId: row.SceneId,
			AuthorDbref: KeyToDbref(row.AuthorKey),
			AuthorName: n["authorName"].As<string>(),
			ShowAsName: n["showAsName"].As<string>(),
			OriginDbref: KeyToDbref(row.OriginKey),
			OriginName: n["originName"].As<string>(),
			Source: n["source"].As<string>(),
			Tags: tags,
			Meta: DeserializeMeta(AsNullableString(n.Properties.GetValueOrDefault("metaJson")) ?? "{}"),
			CreatedAt: n["createdAt"].As<long>(),
			IsDeleted: n["isDeleted"].As<bool>(),
			Content: content,
			Markup: markup,
			EditCount: row.EditCount,
			LastEditedAt: lastEdited,
			LastEditorDbref: lastEditorDbref,
			LastEditorName: lastEditorName);
	}

	private static ScenePoseEdit MapEdit(string poseId, INode node, int? editorKey) => new(
		Id: node["editId"].As<string>(),
		PoseId: poseId,
		Content: node["content"].As<string>(),
		Markup: node["markup"].As<string>(),
		EditorDbref: KeyToDbref(editorKey),
		EditorName: node["editorName"].As<string>(),
		EditedAt: node["editedAt"].As<long>());

	private static SceneMember MapMember(string sceneId, IRelationship rel, int? memberKey) => new(
		SceneId: sceneId,
		MemberDbref: KeyToDbref(memberKey),
		MemberName: rel.Properties.TryGetValue("memberName", out var mn) && mn is not null ? mn.As<string>() : "",
		Role: rel.Properties.TryGetValue("role", out var role) && role is not null ? role.As<string>() : "",
		ShowAs: rel.Properties.TryGetValue("showAs", out var sa) && sa is not null ? sa.As<string>() : "",
		IsCurrent: rel.Properties.TryGetValue("isCurrent", out var ic) && ic is not null && ic.As<bool>(),
		GrantedAt: rel.Properties.TryGetValue("grantedAt", out var ga) && ga is not null ? ga.As<long>() : 0);

	private static ScenePlot MapPlot(INode node, int? ownerKey) => new(
		Id: node["plotId"].As<string>(),
		Title: node["title"].As<string>(),
		Description: node["description"].As<string>(),
		OwnerDbref: KeyToDbref(ownerKey),
		OwnerName: node["ownerName"].As<string>(),
		CreatedAt: node["createdAt"].As<long>(),
		UpdatedAt: node["updatedAt"].As<long>());

	// ── Primitive helpers ─────────────────────────────────────────────────────────

	private static long NowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	/// <summary>Resolves a dbref string (e.g. "#5") to its numeric object key, or null.</summary>
	private static int? ResolveKey(string? dbref)
	{
		if (string.IsNullOrWhiteSpace(dbref)) return null;
		return DBRef.TryParse(dbref, out var parsed) && parsed.HasValue ? parsed.Value.Number : null;
	}

	private static string? KeyToDbref(int? key) => key is null ? null : $"#{key.Value}";

	private static bool ParseBool(string value) =>
		value.Trim() is "1" or "true" or "yes" or "on"
		|| string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

	private static long ParseMillis(string value) =>
		long.TryParse(value.Trim(), out var v) ? v : NoMillis;

	/// <summary>
	/// Splits an inbound content string into a plain (ANSI-stripped) projection and a
	/// raw serialized MString markup, matching the model's Content/Markup contract.
	/// </summary>
	private static (string Plain, string Markup) SplitContent(string content)
	{
		var ms = MModule.single(content ?? "");
		return (MModule.plainText(ms), MModule.serialize(ms));
	}

	private static Dictionary<string, string> DeserializeMeta(string json)
	{
		if (string.IsNullOrWhiteSpace(json) || json == "{}")
			return new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
				?? new Dictionary<string, string>(StringComparer.Ordinal);
		}
		catch (JsonException)
		{
			return new Dictionary<string, string>(StringComparer.Ordinal);
		}
	}

	private static string SerializeMeta(Dictionary<string, string> meta) =>
		JsonSerializer.Serialize(meta, JsonOptions);

	private static int? AsNullableInt(object? value) => value is null ? null : value.As<int>();
	private static long? AsNullableLong(object? value) => value is null ? null : value.As<long>();
	private static string? AsNullableString(object? value) => value is null ? null : value.As<string>();
	private static INode? AsNullableNode(object? value) => value as INode;

	// ── Row carriers ──────────────────────────────────────────────────────────────

	private sealed record SceneRow(INode Node, int? OwnerKey, int? StarterKey, int? RoomKey);

	private sealed record PoseRow(
		INode Node,
		string SceneId,
		int? AuthorKey,
		int? OriginKey,
		INode? CurrentEdit,
		int? EditorKey,
		int EditCount);

	#endregion
}
