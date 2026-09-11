using System.Buffers.Binary;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SceneModel = SharpMUSH.Plugins.Scene.Models.Scene;
using OkNone = SharpMUSH.Library.DiscriminatedUnions.None;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// Lightning (LMDB) storage for the Scene System, written against the host-shared
/// <see cref="ILightningStorageAccessor"/> only — the plugin references neither the provider assembly nor
/// LightningDB. The other three providers speak a query language and express the scene graph as edges;
/// here there is one ordered byte-keyed map per concern, so the graph is spelled out as explicit keys and
/// index rows this class maintains on every write.
/// </summary>
/// <remarks>
/// <para><b>Tables.</b> All plugin-owned and opened once in the constructor —
/// <see cref="ILightningStorageAccessor.OpenTable"/> must never run inside a write job, so it happens
/// there and nowhere else:</para>
/// <list type="table">
///   <item><term><c>scene</c></term><description>scene id → <see cref="SceneRecord"/> JSON.</description></item>
///   <item><term><c>scene.idx</c> (duplicates)</term><description>every listing the contract defines, keyed
///   <c>&lt;kind&gt;\0&lt;discriminator&gt;</c> → scene id: <c>all\0</c> (recent), <c>status\0&lt;status&gt;</c>
///   (active/finished), <c>sched\0</c> (scheduled), <c>room\0&lt;dbref&gt;</c> (a room's scenes, for
///   <c>scenewhere</c>), <c>member\0&lt;dbref&gt;</c> (mine) and <c>plot\0&lt;plotId&gt;</c> (a plot's scenes).</description></item>
///   <item><term><c>scene.part</c></term><description><c>Keys.Composite(sceneId, dbref)</c> →
///   <see cref="SceneMemberRecord"/> JSON, carrying the member's name snapshot. The scene id leads, so one
///   prefix scan is a scene's whole cast.</description></item>
///   <item><term><c>scene.pose</c></term><description><c>Keys.Composite(sceneId, "", seq)</c> →
///   <see cref="ScenePoseRecord"/> JSON. The big-endian <c>seq</c> IS the pose's order — what the other
///   providers keep as a <c>pose_next</c> linked list — so a prefix scan yields a scene's poses in chain
///   order with no traversal.</description></item>
///   <item><term><c>scene.pose.idx</c></term><description>pose id → that pose's <c>scene.pose</c> key. Poses
///   are addressed globally by id (<c>sceneeditpose</c>, <c>sceneundo</c>, …) but stored under their scene.</description></item>
///   <item><term><c>scene.log</c></term><description><c>Keys.Composite(poseId, "", seq)</c> →
///   <see cref="ScenePoseEditRecord"/> JSON: a pose's content-version log, oldest first. The pose record's
///   <c>CurrentEditSeq</c> is the <c>current_edit</c> pointer undo/redo moves.</description></item>
///   <item><term><c>scene.plot</c></term><description>plot id → <see cref="ScenePlotRecord"/> JSON.</description></item>
///   <item><term><c>scene.meta</c></term><description>counters: <c>scene_id</c> and <c>pose_id</c> (the
///   1-based id sequences the other providers get from an autoincrement key generator or a counter record),
///   and <c>poseseq\0&lt;sceneId&gt;</c>, a scene's pose-order sequence.</description></item>
///   <item><term><c>obj</c></term><description>NOT plugin-owned — the provider's own object table, which
///   <c>OpenTable</c> hands back because it is already open. See the name-snapshot note below.</description></item>
/// </list>
///
/// <para><b>Ids.</b> Scene and pose ids are 1-based decimal strings drawn from the <c>scene.meta</c>
/// and SurrealDB's <c>counter:scene_id</c>/<c>counter:pose_id</c> allocate. Plot and pose-edit ids are
/// GUIDs (<c>"N"</c>), as SurrealDB's are. Every id leaves this class bare, with no table prefix: players
/// type scene ids and they are a path segment in <c>/scenes/{id}/live</c>.</para>
///
/// <para><b>Name snapshots.</b> Taken at write time by reading the live object out of the provider's own
/// <c>obj</c> table at <c>Keys.Dbref(n)</c> and lifting one property, <c>Name</c>, off its JSON. That is a
/// deliberate coupling to the provider's <c>ObjectRecord</c> shape: the plugin cannot name that type (it
/// lives in <c>SharpMUSH.Database.Lightning</c>, which the plugin must not reference) and the accessor
/// hands out bytes, not records. The coupling costs one property name — renaming <c>ObjectRecord.Name</c>
/// empties every snapshot taken here, which is why <see cref="ObjectNameRecord"/> says so where it is
/// declared. The provider serializes with <c>PropertyNamingPolicy = Unspecified</c>, so the field is
/// PascalCase and <see cref="JsonOptions"/> matches it.</para>
///
/// <para><b>Transaction discipline.</b> Each mutation is exactly one
/// <see cref="ILightningStorageAccessor.WriteAsync"/> job — no job calls another, and no job calls
/// <c>Read</c> or <c>OpenTable</c> (either would wait on the writer thread that is running it). A job does
/// its own reads through the write transaction it was handed and projects its own return value. Every read
/// materialises inside a single <see cref="ILightningStorageAccessor.Read"/>, because the cursors behind
/// <c>Range</c>/<c>Dups</c> are valid only while that transaction is open.</para>
///
/// <para><b>Sorting and visibility.</b> LMDB orders by key alone, so the orderings the contract specifies —
/// recent-first by <c>LastActivityAt</c>, scheduled ascending inside the UTC-millis window — are applied in
/// C# after the index range is read. Visibility filtering matches the other providers exactly: the viewer
/// scopes <c>mine</c> and nothing else; who may SEE a scene is decided above this layer.</para>
/// </remarks>
public sealed class LightningSceneStorage : ISceneStorage
{
	/// <summary>
	/// Reflection-based and PascalCase. The plugin runs in its own <c>AssemblyLoadContext</c> and cannot
	/// use the provider's source-generated <c>JsonSerializerContext</c>; the naming policy has to agree
	/// with it regardless, so <see cref="ObjectNameRecord"/> can read a row the provider wrote.
	/// </summary>
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = null,
		WriteIndented = false
	};

	private const string IdxAllKind = "all";
	private const string IdxStatusKind = "status";
	private const string IdxScheduledKind = "sched";
	private const string IdxRoomKind = "room";
	private const string IdxMemberKind = "member";
	private const string IdxPlotKind = "plot";
	private const string PoseSeqCounter = "poseseq";

	private static readonly byte[] IdxAll = Keys.Composite(IdxAllKind, "");
	private static readonly byte[] IdxScheduled = Keys.Composite(IdxScheduledKind, "");

	private readonly ILightningStorageAccessor _accessor;
	private readonly TableDef _scenes;
	private readonly TableDef _sceneIdx;
	private readonly TableDef _participants;
	private readonly TableDef _poses;
	private readonly TableDef _poseIdx;
	private readonly TableDef _log;
	private readonly TableDef _plots;
	private readonly TableDef _meta;
	private readonly TableDef _objects;

	public LightningSceneStorage(ILightningStorageAccessor accessor)
	{
		_accessor = accessor;
		_scenes = accessor.OpenTable("scene", duplicates: false);
		_sceneIdx = accessor.OpenTable("scene.idx", duplicates: true);
		_participants = accessor.OpenTable("scene.part", duplicates: false);
		_poses = accessor.OpenTable("scene.pose", duplicates: false);
		_poseIdx = accessor.OpenTable("scene.pose.idx", duplicates: false);
		_log = accessor.OpenTable("scene.log", duplicates: false);
		_plots = accessor.OpenTable("scene.plot", duplicates: false);
		_meta = accessor.OpenTable("scene.meta", duplicates: false);
		// Already in the provider's catalogue: OpenTable is idempotent by name and hands back the
		// definition the store already holds rather than opening a second handle.
		_objects = accessor.OpenTable("obj", duplicates: false);
	}

	#region Stored records

	private sealed record SceneRecord
	{
		public string Id { get; init; } = "";
		public string Status { get; set; } = "new";
		public bool IsPublic { get; set; }
		public bool IsTempRoom { get; set; }
		public long? ScheduledFor { get; set; }
		public long StartedAt { get; init; }
		public long LastActivityAt { get; set; }
		public int PoseCount { get; set; }
		public long? OwnerDbref { get; set; }
		public string OwnerName { get; set; } = "";
		public long? StarterDbref { get; set; }
		public string StarterName { get; set; } = "";
		public long? RoomDbref { get; set; }
		public string RoomName { get; set; } = "";
		public Dictionary<string, string> Meta { get; init; } = [];
	}

	private sealed record ScenePoseRecord
	{
		public string Id { get; init; } = "";
		public string SceneId { get; init; } = "";
		public long? AuthorDbref { get; set; }
		public string AuthorName { get; set; } = "";
		public string ShowAsName { get; set; } = "";
		public long? OriginDbref { get; set; }
		public string OriginName { get; set; } = "";
		public string Source { get; set; } = "";
		public List<string> Tags { get; set; } = [];
		public Dictionary<string, string> Meta { get; init; } = [];
		public long CreatedAt { get; init; }
		public bool IsDeleted { get; set; }

		/// <summary>The <c>current_edit</c> pointer: which <c>scene.log</c> version this pose shows.</summary>
		public uint CurrentEditSeq { get; set; }
	}

	private sealed record ScenePoseEditRecord
	{
		public string Id { get; init; } = "";
		public string PoseId { get; init; } = "";
		public string Content { get; init; } = "";
		public string Markup { get; init; } = "";
		public long? EditorDbref { get; init; }
		public string EditorName { get; init; } = "";
		public long EditedAt { get; init; }
	}

	private sealed record SceneMemberRecord
	{
		public string SceneId { get; init; } = "";
		public long MemberDbref { get; init; }
		public string MemberName { get; set; } = "";
		public string Role { get; set; } = "";
		public string ShowAs { get; set; } = "";
		public bool IsCurrent { get; set; }
		public long GrantedAt { get; init; }
	}

	private sealed record ScenePlotRecord
	{
		public string Id { get; init; } = "";
		public string Title { get; set; } = "";
		public string Description { get; set; } = "";
		public long? OwnerDbref { get; set; }
		public string OwnerName { get; set; } = "";
		public long CreatedAt { get; init; }
		public long UpdatedAt { get; set; }
	}

	/// <summary>
	/// The one property this plugin lifts out of a row the Lightning PROVIDER owns
	/// (<c>SharpMUSH.Database.Lightning.Records.ObjectRecord</c>, in the <c>obj</c> table). Renaming that
	/// record's <c>Name</c> silently empties every name snapshot taken here.
	/// </summary>
	private sealed record ObjectNameRecord
	{
		public string Name { get; init; } = "";
	}

	#endregion

	#region ISceneService — scenes

	public Task<SceneModel> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "")
		=> _accessor.WriteAsync(tx =>
		{
			var now = UtcMillis();
			var id = NextCounter(tx, "scene_id").ToString();
			var owner = DbrefNumber(ownerDbref);
			var room = DbrefNumber(roomDbref);
			var ownerName = ObjectName(tx, owner) ?? "";

			var meta = new Dictionary<string, string>();
			if (!string.IsNullOrEmpty(title))
			{
				meta["title"] = title;
			}

			var record = new SceneRecord
			{
				Id = id,
				// A freshly created scene is "new", not "active" — the create-default every provider shares.
				Status = "new",
				// Public by default: a scene nobody can find is not a scene anyone can join.
				IsPublic = true,
				IsTempRoom = false,
				ScheduledFor = null,
				StartedAt = now,
				LastActivityAt = now,
				PoseCount = 0,
				OwnerDbref = owner,
				OwnerName = ownerName,
				// The starter defaults to the owner.
				StarterDbref = owner,
				StarterName = ownerName,
				RoomDbref = room,
				RoomName = ObjectName(tx, room) ?? "",
				Meta = meta
			};

			tx.Put(_scenes, Keys.Str(id), Encode(record));
			AddIndex(tx, IdxAll, id);
			AddIndex(tx, StatusIndexKey(record.Status), id);
			if (room is { } roomNumber)
			{
				AddIndex(tx, RoomIndexKey(roomNumber), id);
			}

			return ProjectScene(tx, record);
		}).AsTask();

	public Task<Found<SceneModel>> GetSceneAsync(string sceneId)
		=> Task.FromResult(_accessor.Read<Found<SceneModel>>(tx
			=> ReadScene(tx, BareId(sceneId)) is { } scene ? ProjectScene(tx, scene) : new NotFound()));

	public Task<Found<SceneModel>> SetSceneMetaAsync(string sceneId, string key, string value)
		=> _accessor.WriteAsync<Found<SceneModel>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is not { } scene)
			{
				return new NotFound();
			}

			var normalized = key.Trim().ToLowerInvariant();
			switch (normalized)
			{
				case "status":
					RemoveIndex(tx, StatusIndexKey(scene.Status), id);
					scene.Status = value;
					AddIndex(tx, StatusIndexKey(scene.Status), id);
					break;
				case "public":
					scene.IsPublic = ParseBool(value);
					break;
				case "istemp":
					scene.IsTempRoom = ParseBool(value);
					break;
				case "scheduledfor":
					var scheduled = long.TryParse(value, out var millis) ? millis : (long?)null;
					if (scene.ScheduledFor is null && scheduled is not null)
					{
						AddIndex(tx, IdxScheduled, id);
					}
					else if (scene.ScheduledFor is not null && scheduled is null)
					{
						RemoveIndex(tx, IdxScheduled, id);
					}
					scene.ScheduledFor = scheduled;
					break;
				case "room":
					if (scene.RoomDbref is { } oldRoom)
					{
						RemoveIndex(tx, RoomIndexKey(oldRoom), id);
					}
					scene.RoomDbref = DbrefNumber(value);
					scene.RoomName = ObjectName(tx, scene.RoomDbref) ?? "";
					if (scene.RoomDbref is { } newRoom)
					{
						AddIndex(tx, RoomIndexKey(newRoom), id);
					}
					break;
				case "owner":
					scene.OwnerDbref = DbrefNumber(value);
					scene.OwnerName = ObjectName(tx, scene.OwnerDbref) ?? "";
					break;
				case "plot":
					// The value is a plot id: link the scene under it, silently ignoring a plot that is
					// not there — the same no-op the other providers perform from this key.
					LinkPlot(tx, BareId(value), id);
					break;
				default:
					// Known descriptive keys (title, summary, icdate, location, type, warning) and any
					// custom key land in the opaque Meta bag.
					scene.Meta[normalized] = value;
					break;
			}

			scene.LastActivityAt = UtcMillis();
			tx.Put(_scenes, Keys.Str(id), Encode(scene));
			return ProjectScene(tx, scene);
		}).AsTask();

	public Task<IReadOnlyList<SceneModel>> ListScenesAsync(string filter, string? viewerDbref = null,
		long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50)
		=> Task.FromResult(_accessor.Read<IReadOnlyList<SceneModel>>(tx =>
		{
			IEnumerable<SceneRecord> matches;
			switch ((filter ?? "").Trim().ToLowerInvariant())
			{
				case "scheduled":
					matches = ScenesByIndex(tx, IdxScheduled)
						.Where(s => s.ScheduledFor is not null)
						.Where(s => fromUtcMillis is null || s.ScheduledFor >= fromUtcMillis)
						.Where(s => toUtcMillis is null || s.ScheduledFor <= toUtcMillis)
						.OrderBy(s => s.ScheduledFor);
					break;
				case "mine":
					if (DbrefNumber(viewerDbref) is not { } viewer)
					{
						return [];
					}
					matches = ScenesByIndex(tx, MemberIndexKey(viewer)).OrderByDescending(s => s.LastActivityAt);
					break;
				case "active":
					matches = ScenesByIndex(tx, StatusIndexKey("active")).OrderByDescending(s => s.LastActivityAt);
					break;
				case "finished":
					matches = ScenesByIndex(tx, StatusIndexKey("finished")).OrderByDescending(s => s.LastActivityAt);
					break;
				default:
					matches = ScenesByIndex(tx, IdxAll).OrderByDescending(s => s.LastActivityAt);
					break;
			}

			return matches.Take(Math.Max(0, count)).Select(s => ProjectScene(tx, s)).ToList();
		}));

	public Task<Found<SceneModel>> GetActiveSceneInRoomAsync(string roomDbref)
		=> Task.FromResult(_accessor.Read<Found<SceneModel>>(tx =>
		{
			if (DbrefNumber(roomDbref) is not { } room)
			{
				return new NotFound();
			}

			var scene = ScenesByIndex(tx, RoomIndexKey(room))
				.Where(s => string.Equals(s.Status, "active", StringComparison.Ordinal))
				.OrderByDescending(s => s.LastActivityAt)
				.FirstOrDefault();

			return scene is null ? new NotFound() : ProjectScene(tx, scene);
		}));

	#endregion

	#region ISceneService — poses

	public Task<FoundResult<ScenePose>> AddPoseAsync(string sceneId, string authorDbref,
		string showAs, string originDbref, string source, IReadOnlyList<string> tags, string content)
		=> _accessor.WriteAsync<FoundResult<ScenePose>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is not { } scene)
			{
				return new NotFound();
			}

			var now = UtcMillis();
			var author = DbrefNumber(authorDbref);
			var origin = DbrefNumber(originDbref);
			var authorName = ObjectName(tx, author) ?? "";
			var poseId = NextCounter(tx, "pose_id").ToString();
			var seq = NextPoseSeq(tx, id);

			var pose = new ScenePoseRecord
			{
				Id = poseId,
				SceneId = id,
				AuthorDbref = author,
				AuthorName = authorName,
				ShowAsName = showAs ?? "",
				OriginDbref = origin,
				OriginName = ObjectName(tx, origin) ?? "",
				Source = source ?? "",
				Tags = (tags ?? []).ToList(),
				CreatedAt = now,
				IsDeleted = false,
				CurrentEditSeq = 1
			};

			var poseKey = PoseKey(id, seq);
			tx.Put(_poses, poseKey, Encode(pose));
			tx.Put(_poseIdx, Keys.Str(poseId), poseKey);
			WriteEdit(tx, poseId, 1, author, authorName, content, now);

			scene.PoseCount += 1;
			scene.LastActivityAt = now;
			tx.Put(_scenes, Keys.Str(id), Encode(scene));

			return ProjectPose(tx, pose);
		}).AsTask();

	public Task<Found<ScenePose>> GetPoseAsync(string poseId)
		=> Task.FromResult(_accessor.Read<Found<ScenePose>>(tx
			=> ReadPose(tx, BareId(poseId)) is { } found ? ProjectPose(tx, found.Pose) : new NotFound()));

	public Task<Found<IReadOnlyList<ScenePose>>> GetPosesAsync(string sceneId,
		string? authorDbref = null, int? count = null)
		=> Task.FromResult(_accessor.Read<Found<IReadOnlyList<ScenePose>>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is null)
			{
				return new NotFound();
			}

			// Filtered on the projected, live-resolved author: a pose whose author has since been destroyed
			// carries a null AuthorDbref and matches nobody, the same as the other providers.
			var author = DbrefNumber(authorDbref) is { } number ? $"#{number}" : null;
			var poses = ScenePoses(tx, id)
				.Select(entry => ProjectPose(tx, entry.Pose))
				.Where(p => author is null || p.AuthorDbref == author)
				.ToList();

			// The last `count` poses: drop the head in place rather than copying the tail out.
			if (count is { } limit && limit >= 0 && poses.Count > limit)
			{
				poses.RemoveRange(0, poses.Count - limit);
			}

			return poses;
		}));

	public Task<Found<ScenePose>> SetPoseMetaAsync(string poseId, string key, string value)
		=> _accessor.WriteAsync<Found<ScenePose>>(tx =>
		{
			if (ReadPose(tx, BareId(poseId)) is not { } found)
			{
				return new NotFound();
			}

			var pose = found.Pose;
			var normalized = key.Trim().ToLowerInvariant();
			switch (normalized)
			{
				case "showas":
					pose.ShowAsName = value;
					break;
				case "authorname":
					pose.AuthorName = value;
					break;
				case "author":
					pose.AuthorDbref = DbrefNumber(value);
					pose.AuthorName = ObjectName(tx, pose.AuthorDbref) ?? "";
					break;
				case "origin":
					pose.OriginDbref = DbrefNumber(value);
					pose.OriginName = ObjectName(tx, pose.OriginDbref) ?? "";
					break;
				case "originname":
					pose.OriginName = value;
					break;
				case "source":
					pose.Source = value;
					break;
				case "tags":
					pose.Tags = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
					break;
				default:
					pose.Meta[normalized] = value;
					break;
			}

			tx.Put(_poses, found.Key, Encode(pose));
			return ProjectPose(tx, pose);
		}).AsTask();

	public Task<Found<ScenePose>> EditPoseAsync(string poseId, string editorDbref, string content)
		=> _accessor.WriteAsync<Found<ScenePose>>(tx =>
		{
			if (ReadPose(tx, BareId(poseId)) is not { } found)
			{
				return new NotFound();
			}

			var pose = found.Pose;
			var now = UtcMillis();
			var editor = DbrefNumber(editorDbref);

			// A fresh edit truncates the redo-forward versions, which is what makes the next sequence
			// after the pointer always free.
			foreach (var forward in PoseEdits(tx, pose.Id).Where(e => e.Seq > pose.CurrentEditSeq))
			{
				tx.Delete(_log, forward.Key);
			}

			var seq = pose.CurrentEditSeq + 1;
			WriteEdit(tx, pose.Id, seq, editor, ObjectName(tx, editor) ?? "", content, now);
			pose.CurrentEditSeq = seq;
			tx.Put(_poses, found.Key, Encode(pose));

			return ProjectPose(tx, pose);
		}).AsTask();

	public Task<FoundResult<ScenePose>> UndoPoseAsync(string poseId)
		=> MoveEditPointerAsync(poseId, -1, "Already at the oldest version.");

	public Task<FoundResult<ScenePose>> RedoPoseAsync(string poseId)
		=> MoveEditPointerAsync(poseId, +1, "Already at the newest version.");

	public Task<FoundResult<ScenePose>> MovePoseAsync(string poseId, string afterPoseId)
		=> _accessor.WriteAsync<FoundResult<ScenePose>>(tx =>
		{
			var id = BareId(poseId);
			if (ReadPose(tx, id) is not { } found)
			{
				return new NotFound();
			}

			var sceneId = found.Pose.SceneId;
			if (sceneId.Length == 0 || ReadScene(tx, sceneId) is not { } scene)
			{
				return new Error<string>("Pose is not attached to a scene.");
			}

			string? afterId = null;
			if (!string.IsNullOrWhiteSpace(afterPoseId))
			{
				afterId = BareId(afterPoseId);
				if (ReadPose(tx, afterId) is not { } after
						|| !string.Equals(after.Pose.SceneId, sceneId, StringComparison.Ordinal))
				{
					return new Error<string>("The two poses are not in the same scene.");
				}
				if (string.Equals(afterId, id, StringComparison.Ordinal))
				{
					return new Error<string>("Cannot move a pose after itself.");
				}
			}

			// The order lives in the key, so a move is a renumber: take the scene's poses in their
			// current order, re-slot the moved one, and write every pose back under a fresh 1..N
			// sequence. Pose VALUES move verbatim — only their keys change.
			var ordered = ScenePoses(tx, sceneId);
			var from = ordered.FindIndex(e => string.Equals(e.Pose.Id, id, StringComparison.Ordinal));
			if (from < 0)
			{
				return new Error<string>("Pose is not in its scene's pose chain.");
			}
			var moved = ordered[from];
			ordered.RemoveAt(from);
			var insertAt = afterId is null
				? 0
				: ordered.FindIndex(e => string.Equals(e.Pose.Id, afterId, StringComparison.Ordinal)) + 1;
			ordered.Insert(insertAt, moved);

			foreach (var entry in ordered)
			{
				tx.Delete(_poses, entry.Key);
			}

			var seq = 0u;
			foreach (var entry in ordered)
			{
				var key = PoseKey(sceneId, ++seq);
				tx.Put(_poses, key, entry.Value);
				tx.Put(_poseIdx, Keys.Str(entry.Pose.Id), key);
			}
			SetPoseSeq(tx, sceneId, seq);

			scene.LastActivityAt = UtcMillis();
			tx.Put(_scenes, Keys.Str(sceneId), Encode(scene));

			return ProjectPose(tx, moved.Pose);
		}).AsTask();

	public Task<Found<ScenePose>> DeletePoseAsync(string poseId)
		=> _accessor.WriteAsync<Found<ScenePose>>(tx =>
		{
			if (ReadPose(tx, BareId(poseId)) is not { } found)
			{
				return new NotFound();
			}

			var pose = found.Pose;
			// Soft delete: the slot stays in the chain so the poses around it keep their order.
			pose.IsDeleted = true;
			tx.Put(_poses, found.Key, Encode(pose));

			if (ReadScene(tx, pose.SceneId) is { } scene)
			{
				scene.PoseCount = Math.Max(0, scene.PoseCount - 1);
				scene.LastActivityAt = UtcMillis();
				tx.Put(_scenes, Keys.Str(scene.Id), Encode(scene));
			}

			return ProjectPose(tx, pose);
		}).AsTask();

	public Task<Found<IReadOnlyList<ScenePoseEdit>>> GetPoseEditsAsync(string poseId)
		=> Task.FromResult(_accessor.Read<Found<IReadOnlyList<ScenePoseEdit>>>(tx =>
		{
			if (ReadPose(tx, BareId(poseId)) is not { } found)
			{
				return new NotFound();
			}

			var edits = PoseEdits(tx, found.Pose.Id).Select(e => ProjectEdit(tx, e.Record)).ToList();
			return edits;
		}));

	public Task<Found<IReadOnlyList<string>>> GetTagsAsync(string sceneId)
		=> Task.FromResult(_accessor.Read<Found<IReadOnlyList<string>>>(tx
			=> LivePoses(tx, BareId(sceneId)) is not { } poses
				? new NotFound()
				: poses
					.SelectMany(p => p.Tags)
					.Where(t => !string.IsNullOrWhiteSpace(t))
					.Distinct(StringComparer.Ordinal)
					.ToList()));

	public Task<Found<IReadOnlyList<string>>> GetCastAsync(string sceneId)
		=> Task.FromResult(_accessor.Read<Found<IReadOnlyList<string>>>(tx
			=> LivePoses(tx, BareId(sceneId)) is not { } poses
				? new NotFound()
				: poses
					.Select(p => string.IsNullOrEmpty(p.ShowAsName) ? p.AuthorName : p.ShowAsName)
					.Where(n => !string.IsNullOrWhiteSpace(n))
					.Distinct(StringComparer.Ordinal)
					.ToList()));

	#endregion

	#region ISceneService — membership

	public Task<Found<SceneMember>> AddMemberAsync(string sceneId, string playerDbref, string role)
		=> _accessor.WriteAsync<Found<SceneMember>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is null || DbrefNumber(playerDbref) is not { } player)
			{
				return new NotFound();
			}

			var key = Keys.Composite(id, player);
			var name = ObjectName(tx, player) ?? "";

			// Update in place rather than replace. This row does not only carry the role: IsCurrent IS
			// the player's focus and ShowAs is their +scene/as persona, so recreating it would silently
			// reset both — and nearly every owner verb acts on scenefocus(%#).
			SceneMemberRecord member;
			if (tx.TryGet(_participants, key, out var existing))
			{
				member = Decode<SceneMemberRecord>(existing);
				member.Role = role ?? "";
				member.MemberName = name;
			}
			else
			{
				member = new SceneMemberRecord
				{
					SceneId = id,
					MemberDbref = player,
					MemberName = name,
					Role = role ?? "",
					ShowAs = "",
					IsCurrent = false,
					GrantedAt = UtcMillis()
				};
				AddIndex(tx, MemberIndexKey(player), id);
			}

			tx.Put(_participants, key, Encode(member));
			return ProjectMember(tx, member);
		}).AsTask();

	public Task<Found<OkNone>> RemoveMemberAsync(string sceneId, string playerDbref)
		=> _accessor.WriteAsync<Found<OkNone>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is null)
			{
				return new NotFound();
			}
			if (DbrefNumber(playerDbref) is not { } player)
			{
				return new OkNone();
			}

			tx.Delete(_participants, Keys.Composite(id, player));
			RemoveIndex(tx, MemberIndexKey(player), id);
			return new OkNone();
		}).AsTask();

	public Task<Found<IReadOnlyList<SceneMember>>> GetMembersAsync(string sceneId, string? role = null)
		=> Task.FromResult(_accessor.Read<Found<IReadOnlyList<SceneMember>>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is null)
			{
				return new NotFound();
			}

			var members = SceneMembers(tx, id)
				.Where(m => string.IsNullOrWhiteSpace(role) || string.Equals(m.Role, role, StringComparison.Ordinal))
				.Select(m => ProjectMember(tx, m))
				.ToList();
			return members;
		}));

	public Task<Found<SceneMember>> GetMemberAsync(string sceneId, string playerDbref)
		=> Task.FromResult(_accessor.Read<Found<SceneMember>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is null || DbrefNumber(playerDbref) is not { } player)
			{
				return new NotFound();
			}

			return tx.TryGet(_participants, Keys.Composite(id, player), out var bytes)
				? ProjectMember(tx, Decode<SceneMemberRecord>(bytes))
				: new NotFound();
		}));

	public Task<Found<OkNone>> SetFocusAsync(string playerDbref, string? sceneId = null)
		=> _accessor.WriteAsync<Found<OkNone>>(tx =>
		{
			if (DbrefNumber(playerDbref) is not { } player)
			{
				return new NotFound();
			}

			// Clear the focus flag on every membership the player holds first, so a re-focus that then
			// fails leaves them with no current scene rather than two.
			foreach (var id in MemberScenes(tx, player))
			{
				var key = Keys.Composite(id, player);
				if (!tx.TryGet(_participants, key, out var bytes))
				{
					continue;
				}

				var existing = Decode<SceneMemberRecord>(bytes);
				if (!existing.IsCurrent)
				{
					continue;
				}

				existing.IsCurrent = false;
				tx.Put(_participants, key, Encode(existing));
			}

			if (string.IsNullOrWhiteSpace(sceneId))
			{
				return new OkNone();
			}

			var target = BareId(sceneId);
			if (ReadScene(tx, target) is null)
			{
				return new NotFound();
			}

			// Focusing a player who is not yet in the cast auto-creates a role-less membership so the
			// focus sticks; a bare update would no-op and leave them with no current scene.
			var memberKey = Keys.Composite(target, player);
			SceneMemberRecord member;
			if (tx.TryGet(_participants, memberKey, out var found))
			{
				member = Decode<SceneMemberRecord>(found);
			}
			else
			{
				member = new SceneMemberRecord
				{
					SceneId = target,
					MemberDbref = player,
					MemberName = ObjectName(tx, player) ?? "",
					Role = "",
					ShowAs = "",
					GrantedAt = UtcMillis()
				};
				AddIndex(tx, MemberIndexKey(player), target);
			}

			member.IsCurrent = true;
			tx.Put(_participants, memberKey, Encode(member));
			return new OkNone();
		}).AsTask();

	public Task<Found<SceneModel>> GetCurrentSceneAsync(string playerDbref)
		=> Task.FromResult(_accessor.Read<Found<SceneModel>>(tx =>
		{
			if (DbrefNumber(playerDbref) is not { } player)
			{
				return new NotFound();
			}

			foreach (var id in MemberScenes(tx, player))
			{
				if (tx.TryGet(_participants, Keys.Composite(id, player), out var bytes)
						&& Decode<SceneMemberRecord>(bytes).IsCurrent
						&& ReadScene(tx, id) is { } scene)
				{
					return ProjectScene(tx, scene);
				}
			}

			return new NotFound();
		}));

	public Task<Found<SceneMember>> SetShowAsAsync(string sceneId, string playerDbref, string showAs)
		=> _accessor.WriteAsync<Found<SceneMember>>(tx =>
		{
			var id = BareId(sceneId);
			if (ReadScene(tx, id) is null || DbrefNumber(playerDbref) is not { } player)
			{
				return new NotFound();
			}

			var key = Keys.Composite(id, player);
			if (!tx.TryGet(_participants, key, out var bytes))
			{
				return new NotFound();
			}

			var member = Decode<SceneMemberRecord>(bytes);
			member.ShowAs = showAs ?? "";
			tx.Put(_participants, key, Encode(member));
			return ProjectMember(tx, member);
		}).AsTask();

	#endregion

	#region ISceneService — plots

	public Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref)
		=> _accessor.WriteAsync(tx =>
		{
			var now = UtcMillis();
			var owner = DbrefNumber(ownerDbref);
			var id = string.IsNullOrWhiteSpace(plotId) ? Guid.NewGuid().ToString("N") : BareId(plotId);

			var plot = tx.TryGet(_plots, Keys.Str(id), out var bytes)
				? Decode<ScenePlotRecord>(bytes)
				: new ScenePlotRecord { Id = id, CreatedAt = now };

			plot.Title = title ?? "";
			plot.Description = description ?? "";
			plot.OwnerDbref = owner;
			plot.OwnerName = ObjectName(tx, owner) ?? "";
			plot.UpdatedAt = now;

			tx.Put(_plots, Keys.Str(id), Encode(plot));
			return ProjectPlot(tx, plot);
		}).AsTask();

	public Task<Found<ScenePlot>> GetPlotAsync(string plotId)
		=> Task.FromResult(_accessor.Read<Found<ScenePlot>>(tx
			=> tx.TryGet(_plots, Keys.Str(BareId(plotId)), out var bytes)
				? ProjectPlot(tx, Decode<ScenePlotRecord>(bytes))
				: new NotFound()));

	public Task<Found<OkNone>> LinkSceneToPlotAsync(string plotId, string sceneId)
		=> _accessor.WriteAsync<Found<OkNone>>(tx =>
		{
			var plot = BareId(plotId);
			var scene = BareId(sceneId);
			if (!tx.TryGet(_plots, Keys.Str(plot), out _) || ReadScene(tx, scene) is null)
			{
				return new NotFound();
			}

			LinkPlot(tx, plot, scene);
			return new OkNone();
		}).AsTask();

	public Task<Found<OkNone>> UnlinkSceneFromPlotAsync(string plotId, string sceneId)
		=> _accessor.WriteAsync<Found<OkNone>>(tx =>
		{
			var plot = BareId(plotId);
			var scene = BareId(sceneId);
			if (!tx.TryGet(_plots, Keys.Str(plot), out _) || ReadScene(tx, scene) is null)
			{
				return new NotFound();
			}

			RemoveIndex(tx, PlotIndexKey(plot), scene);
			return new OkNone();
		}).AsTask();

	#endregion

	#region Internals — keys, ids and indexes

	private static long UtcMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	private static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

	private static T Decode<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, JsonOptions)!;

	/// <summary>
	/// The bare storage key for an id a caller may spell any of the ways the other providers accept —
	/// <c>1</c>, <c>scene:1</c> or <c>node_sharp_sys_scene_scenes/1</c>. Only the last segment is ours.
	/// </summary>
	private static string BareId(string? id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return "";
		}

		var trimmed = id.Trim();
		var separator = trimmed.LastIndexOfAny([':', '/']);
		return separator < 0 ? trimmed : trimmed[(separator + 1)..];
	}

	/// <summary>The numeric object key behind a dbref string (<c>#5</c>), or null if blank/unparseable.</summary>
	private static long? DbrefNumber(string? dbref)
		=> string.IsNullOrWhiteSpace(dbref)
			? null
			: DBRef.TryParse(dbref, out var parsed) && parsed.HasValue ? parsed.Value.Number : null;

	private static byte[] PoseKey(string sceneId, uint seq) => Keys.Composite(sceneId, "", seq);

	private static byte[] ScenePosePrefix(string sceneId) => Keys.Concat(Keys.Str(sceneId), Keys.Sep, Keys.Sep);

	private static byte[] StatusIndexKey(string status) => Keys.Composite(IdxStatusKind, status);

	private static byte[] RoomIndexKey(long dbref) => Keys.Concat(Keys.Str(IdxRoomKind), Keys.Sep, Keys.Dbref(dbref));

	private static byte[] MemberIndexKey(long dbref) => Keys.Concat(Keys.Str(IdxMemberKind), Keys.Sep, Keys.Dbref(dbref));

	private static byte[] PlotIndexKey(string plotId) => Keys.Composite(IdxPlotKind, plotId);

	private void AddIndex(ITx tx, byte[] indexKey, string sceneId) => tx.Put(_sceneIdx, indexKey, Keys.Str(sceneId));

	private void RemoveIndex(ITx tx, byte[] indexKey, string sceneId) => tx.Delete(_sceneIdx, indexKey, Keys.Str(sceneId));

	/// <summary>Idempotent scene → plot membership; the index row is the whole link.</summary>
	private void LinkPlot(ITx tx, string plotId, string sceneId)
	{
		if (tx.TryGet(_plots, Keys.Str(plotId), out _))
		{
			AddIndex(tx, PlotIndexKey(plotId), sceneId);
		}
	}

	private long NextCounter(ITx tx, string name)
	{
		var key = Keys.Str(name);
		var next = (tx.TryGet(_meta, key, out var bytes) ? Keys.ReadDbref(bytes) : 0) + 1;
		tx.Put(_meta, key, Keys.Dbref(next));
		return next;
	}

	private uint NextPoseSeq(ITx tx, string sceneId)
	{
		var key = Keys.Composite(PoseSeqCounter, sceneId);
		var next = (tx.TryGet(_meta, key, out var bytes) ? Keys.ReadDbref(bytes) : 0) + 1;
		tx.Put(_meta, key, Keys.Dbref(next));
		return (uint)next;
	}

	private void SetPoseSeq(ITx tx, string sceneId, uint highest)
		=> tx.Put(_meta, Keys.Composite(PoseSeqCounter, sceneId), Keys.Dbref(highest));

	private static bool ParseBool(string value) =>
		value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

	/// <summary>The 4-byte big-endian sequence a <c>scene.pose</c>/<c>scene.log</c> key ends with.</summary>
	private static uint SeqOf(byte[] key) => BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(^4));

	#endregion

	#region Internals — reads

	private SceneRecord? ReadScene(ITx tx, string sceneId) =>
		sceneId.Length > 0 && tx.TryGet(_scenes, Keys.Str(sceneId), out var bytes)
			? Decode<SceneRecord>(bytes)
			: null;

	private List<SceneRecord> ScenesByIndex(ITx tx, byte[] indexKey) =>
		tx.Dups(_sceneIdx, indexKey)
			.Select(id => Keys.ReadStr(id))
			.Distinct(StringComparer.Ordinal)
			.Select(id => ReadScene(tx, id))
			.OfType<SceneRecord>()
			.ToList();

	private List<string> MemberScenes(ITx tx, long player) =>
		tx.Dups(_sceneIdx, MemberIndexKey(player))
			.Select(id => Keys.ReadStr(id))
			.Distinct(StringComparer.Ordinal)
			.ToList();

	private (byte[] Key, byte[] Value, ScenePoseRecord Pose)? ReadPose(ITx tx, string poseId)
	{
		if (poseId.Length == 0 || !tx.TryGet(_poseIdx, Keys.Str(poseId), out var poseKey))
		{
			return null;
		}

		return tx.TryGet(_poses, poseKey, out var value) ? (poseKey, value, Decode<ScenePoseRecord>(value)) : null;
	}

	/// <summary>A scene's poses in chain order — the key's trailing sequence IS the order.</summary>
	private List<(byte[] Key, byte[] Value, ScenePoseRecord Pose)> ScenePoses(ITx tx, string sceneId) =>
		tx.Range(_poses, ScenePosePrefix(sceneId))
			.Select(e => (e.Key, e.Value, Decode<ScenePoseRecord>(e.Value)))
			.ToList();

	/// <summary>A scene's non-deleted poses, projected; null when the scene itself is missing.</summary>
	private List<ScenePose>? LivePoses(ITx tx, string sceneId) =>
		ReadScene(tx, sceneId) is null
			? null
			: ScenePoses(tx, sceneId)
				.Where(e => !e.Pose.IsDeleted)
				.Select(e => ProjectPose(tx, e.Pose))
				.ToList();

	private List<SceneMemberRecord> SceneMembers(ITx tx, string sceneId) =>
		tx.Range(_participants, Keys.Concat(Keys.Str(sceneId), Keys.Sep))
			.Select(e => Decode<SceneMemberRecord>(e.Value))
			.ToList();

	private static byte[] PoseLogPrefix(string poseId) => Keys.Concat(Keys.Str(poseId), Keys.Sep, Keys.Sep);

	/// <summary>A pose's content versions, oldest first.</summary>
	private List<(byte[] Key, uint Seq, ScenePoseEditRecord Record)> PoseEdits(ITx tx, string poseId) =>
		tx.Range(_log, PoseLogPrefix(poseId))
			.Select(e => (e.Key, SeqOf(e.Key), Decode<ScenePoseEditRecord>(e.Value)))
			.ToList();

	/// <summary>
	/// The version a pose currently shows and how many it has, from one pass over its log — the sequence
	/// sits in the key, so every version except the shown one is counted without being decoded.
	/// </summary>
	private (int Count, ScenePoseEditRecord? Current) CurrentEdit(ITx tx, string poseId, uint currentSeq)
	{
		var count = 0;
		ScenePoseEditRecord? current = null;
		foreach (var (key, value) in tx.Range(_log, PoseLogPrefix(poseId)))
		{
			count++;
			if (SeqOf(key) == currentSeq)
			{
				current = Decode<ScenePoseEditRecord>(value);
			}
		}

		return (count, current);
	}

	/// <summary>The live object's name at <paramref name="dbref"/>, or null when there is no such object.</summary>
	private string? ObjectName(ITx tx, long? dbref) =>
		dbref is { } number && tx.TryGet(_objects, Keys.Dbref(number), out var bytes)
			? Decode<ObjectNameRecord>(bytes).Name
			: null;

	/// <summary>The dbref as the models spell it, or null when the object it named is gone.</summary>
	private string? LiveDbref(ITx tx, long? dbref) =>
		dbref is { } number && tx.TryGet(_objects, Keys.Dbref(number), out _) ? $"#{number}" : null;

	#endregion

	#region Internals — writes and projections

	private void WriteEdit(ITx tx, string poseId, uint seq, long? editor, string editorName, string? content, long now)
	{
		var split = ScenePoseContent.Split(content);
		tx.Put(_log, Keys.Composite(poseId, "", seq), Encode(new ScenePoseEditRecord
		{
			Id = Guid.NewGuid().ToString("N"),
			PoseId = poseId,
			Content = split.Plain,
			Markup = split.Markup,
			EditorDbref = editor,
			EditorName = editorName,
			EditedAt = now
		}));
	}

	private Task<FoundResult<ScenePose>> MoveEditPointerAsync(string poseId, int step, string atEnd)
		=> _accessor.WriteAsync<FoundResult<ScenePose>>(tx =>
		{
			if (ReadPose(tx, BareId(poseId)) is not { } found)
			{
				return new NotFound();
			}

			var pose = found.Pose;
			var edits = PoseEdits(tx, pose.Id);
			var index = edits.FindIndex(e => e.Seq == pose.CurrentEditSeq);
			if (index < 0)
			{
				return new Error<string>("No edit history for this pose.");
			}

			var target = index + step;
			if (target < 0 || target >= edits.Count)
			{
				return new Error<string>(atEnd);
			}

			pose.CurrentEditSeq = edits[target].Seq;
			tx.Put(_poses, found.Key, Encode(pose));
			return ProjectPose(tx, pose);
		}).AsTask();

	private SceneModel ProjectScene(ITx tx, SceneRecord rec) => new(
		Id: rec.Id,
		Status: rec.Status,
		IsPublic: rec.IsPublic,
		IsTempRoom: rec.IsTempRoom,
		ScheduledFor: rec.ScheduledFor,
		StartedAt: rec.StartedAt,
		LastActivityAt: rec.LastActivityAt,
		PoseCount: rec.PoseCount,
		OwnerDbref: LiveDbref(tx, rec.OwnerDbref),
		OwnerName: rec.OwnerName,
		StarterDbref: LiveDbref(tx, rec.StarterDbref),
		StarterName: rec.StarterName,
		RoomDbref: LiveDbref(tx, rec.RoomDbref),
		RoomName: rec.RoomName,
		Meta: rec.Meta);

	private ScenePose ProjectPose(ITx tx, ScenePoseRecord rec)
	{
		var (versions, current) = CurrentEdit(tx, rec.Id, rec.CurrentEditSeq);
		// "Edited" iff more than one version exists — an unedited pose reports no editor at all.
		var edited = versions > 1 && current is not null;

		return new ScenePose(
			Id: rec.Id,
			SceneId: rec.SceneId,
			AuthorDbref: LiveDbref(tx, rec.AuthorDbref),
			AuthorName: rec.AuthorName,
			ShowAsName: rec.ShowAsName,
			OriginDbref: LiveDbref(tx, rec.OriginDbref),
			OriginName: rec.OriginName,
			Source: rec.Source,
			Tags: rec.Tags,
			Meta: rec.Meta,
			CreatedAt: rec.CreatedAt,
			IsDeleted: rec.IsDeleted,
			Content: current?.Content ?? "",
			Markup: current?.Markup ?? "",
			EditCount: Math.Max(1, versions),
			LastEditedAt: edited ? current!.EditedAt : null,
			LastEditorDbref: edited ? LiveDbref(tx, current!.EditorDbref) : null,
			LastEditorName: edited ? current!.EditorName : null);
	}

	private ScenePoseEdit ProjectEdit(ITx tx, ScenePoseEditRecord rec) => new(
		Id: rec.Id,
		PoseId: rec.PoseId,
		Content: rec.Content,
		Markup: rec.Markup,
		EditorDbref: LiveDbref(tx, rec.EditorDbref),
		EditorName: rec.EditorName,
		EditedAt: rec.EditedAt);

	private SceneMember ProjectMember(ITx tx, SceneMemberRecord rec) => new(
		SceneId: rec.SceneId,
		MemberDbref: LiveDbref(tx, rec.MemberDbref),
		MemberName: rec.MemberName,
		Role: rec.Role,
		ShowAs: rec.ShowAs,
		IsCurrent: rec.IsCurrent,
		GrantedAt: rec.GrantedAt);

	private ScenePlot ProjectPlot(ITx tx, ScenePlotRecord rec) => new(
		Id: rec.Id,
		Title: rec.Title,
		Description: rec.Description,
		OwnerDbref: LiveDbref(tx, rec.OwnerDbref),
		OwnerName: rec.OwnerName,
		CreatedAt: rec.CreatedAt,
		UpdatedAt: rec.UpdatedAt);

	#endregion
}
