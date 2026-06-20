using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Scene;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The wizard-only primitive surface for the graph-native Scene System
/// (<c>graph_sharp_sys_scene</c>). The engine ships these primitives; capture,
/// permission, formatting, and temp-room orchestration are softcode policy.
/// </summary>
/// <remarks>
/// <para>
/// All references to game objects are taken as <b>dbrefs</b>; the implementation
/// resolves the live vertex, manages the edge, and snapshots the object's name
/// (so a later deletion still renders). All timestamps are UTC Unix-millis.
/// <see cref="Scene.Status"/> is a free string. There is <b>no in-memory
/// implementation</b> — the three database providers (ArangoDB, Memgraph,
/// SurrealDB) implement this interface as a side-effect of implementing
/// <c>ISharpDatabase</c>; it is exercised by integration tests.
/// </para>
/// <para>
/// Methods that may miss return <c>OneOf&lt;T, NotFound&gt;</c>; methods that can
/// fail on invalid state add <c>Error&lt;string&gt;</c>.
/// </para>
/// </remarks>
public interface ISceneService
{
	// ── Scenes ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Creates a scene owned by <paramref name="ownerDbref"/> (starter defaults to
	/// the owner). Pass an empty <paramref name="roomDbref"/> for a roomless
	/// scheduled scene; a room is bound later via <c>SetSceneMetaAsync(id,"room",…)</c>.
	/// </summary>
	Task<Scene> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "");

	/// <summary>Returns a scene by id, or <c>NotFound</c>.</summary>
	Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId);

	/// <summary>
	/// Sets one scene metadata key. Known keys (<c>status</c>, <c>public</c>,
	/// <c>scheduledfor</c>, <c>istemp</c>, <c>room</c>, <c>owner</c>, <c>plot</c>,
	/// <c>title</c>, <c>summary</c>, <c>icdate</c>, <c>location</c>, <c>type</c>,
	/// <c>warning</c>) route to the first-class field/edge; any other key lands in
	/// the opaque <see cref="Scene.Meta"/> bag. Returns <c>NotFound</c> if missing.
	/// </summary>
	Task<OneOf<Scene, NotFound>> SetSceneMetaAsync(string sceneId, string key, string value);

	/// <summary>
	/// Lists scenes by <paramref name="filter"/> (<c>active</c> | <c>recent</c> |
	/// <c>scheduled</c> | <c>mine</c>), recent-first; <c>scheduled</c> is sorted by
	/// <see cref="Scene.ScheduledFor"/> ascending and windowed by the optional
	/// UTC-millis bounds. <paramref name="viewerDbref"/> scopes <c>mine</c> and
	/// visibility filtering.
	/// </summary>
	Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null,
		long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50);

	/// <summary>
	/// Returns the <c>active</c> scene bound to <paramref name="roomDbref"/> (the
	/// <c>in_room</c> edge + <c>status=active</c>), or <c>NotFound</c>. (<c>scenewhere</c>.)
	/// </summary>
	Task<OneOf<Scene, NotFound>> GetActiveSceneInRoomAsync(string roomDbref);

	// ── Poses ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Appends a pose (slot + first edit) to the end of the scene's <c>pose_next</c>
	/// chain. <paramref name="showAs"/> blank = shown as the author.
	/// <c>NotFound</c> if the scene is missing; <c>Error</c> on invalid input.
	/// </summary>
	Task<OneOf<ScenePose, NotFound, Error<string>>> AddPoseAsync(string sceneId, string authorDbref,
		string showAs, string originDbref, string source, IReadOnlyList<string> tags, string content);

	/// <summary>Returns a pose by its (globally-unique) id, or <c>NotFound</c>.</summary>
	Task<OneOf<ScenePose, NotFound>> GetPoseAsync(string poseId);

	/// <summary>
	/// Returns a scene's poses in <c>pose_next</c> order. Optionally filtered to one
	/// author and/or limited to the last <paramref name="count"/>. <c>NotFound</c>
	/// if the scene is missing.
	/// </summary>
	Task<OneOf<IReadOnlyList<ScenePose>, NotFound>> GetPosesAsync(string sceneId,
		string? authorDbref = null, int? count = null);

	/// <summary>
	/// Sets one pose metadata key (<c>showas</c>, <c>authorname</c>, <c>author</c>,
	/// <c>origin</c>, <c>originname</c>, <c>source</c>, <c>tags</c>, or custom →
	/// <see cref="ScenePose.Meta"/>). Not for content — use <see cref="EditPoseAsync"/>.
	/// </summary>
	Task<OneOf<ScenePose, NotFound>> SetPoseMetaAsync(string poseId, string key, string value);

	/// <summary>
	/// Edits a pose's content: appends a new <see cref="ScenePoseEdit"/> version,
	/// advances the <c>current_edit</c> pointer, and truncates any redo-forward
	/// versions. <c>NotFound</c> if the pose is missing.
	/// </summary>
	Task<OneOf<ScenePose, NotFound>> EditPoseAsync(string poseId, string editorDbref, string content);

	/// <summary>Moves the <c>current_edit</c> pointer to the previous version. <c>Error</c> if at the oldest.</summary>
	Task<OneOf<ScenePose, NotFound, Error<string>>> UndoPoseAsync(string poseId);

	/// <summary>Moves the <c>current_edit</c> pointer to the next version. <c>Error</c> if at the newest.</summary>
	Task<OneOf<ScenePose, NotFound, Error<string>>> RedoPoseAsync(string poseId);

	/// <summary>
	/// Re-links the pose to follow <paramref name="afterPoseId"/> in the
	/// <c>pose_next</c> chain (empty = move to the front). <c>Error</c> if the two
	/// poses are not in the same scene.
	/// </summary>
	Task<OneOf<ScenePose, NotFound, Error<string>>> MovePoseAsync(string poseId, string afterPoseId);

	/// <summary>Soft-deletes a pose (the slot remains in the chain). <c>NotFound</c> if missing.</summary>
	Task<OneOf<ScenePose, NotFound>> DeletePoseAsync(string poseId);

	/// <summary>Returns a pose's content-version history (oldest first). <c>NotFound</c> if the pose is missing.</summary>
	Task<OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>> GetPoseEditsAsync(string poseId);

	// ── Members / focus ─────────────────────────────────────────────────────────

	/// <summary>
	/// Adds (or updates) the player's <c>member</c> edge with the free-string
	/// <paramref name="role"/>. <c>NotFound</c> if the scene is missing.
	/// </summary>
	Task<OneOf<SceneMember, NotFound>> AddMemberAsync(string sceneId, string playerDbref, string role);

	/// <summary>Removes <b>all</b> of the player's membership to the scene. <c>NotFound</c> if the scene is missing.</summary>
	Task<OneOf<None, NotFound>> RemoveMemberAsync(string sceneId, string playerDbref);

	/// <summary>Returns a scene's members, optionally filtered to one role. <c>NotFound</c> if the scene is missing.</summary>
	Task<OneOf<IReadOnlyList<SceneMember>, NotFound>> GetMembersAsync(string sceneId, string? role = null);

	/// <summary>Returns one player's membership edge for a scene, or <c>NotFound</c>.</summary>
	Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref);

	/// <summary>
	/// Sets the player's current (focused) scene — clears <c>isCurrent</c> on the
	/// player's other member edges. Pass a null/empty <paramref name="sceneId"/> to
	/// clear focus entirely. <c>NotFound</c> if a non-empty scene is missing.
	/// </summary>
	Task<OneOf<None, NotFound>> SetFocusAsync(string playerDbref, string? sceneId = null);

	/// <summary>Returns the player's currently-focused scene, or <c>NotFound</c>. (<c>scenefocus</c>.)</summary>
	Task<OneOf<Scene, NotFound>> GetCurrentSceneAsync(string playerDbref);

	/// <summary>Sets the player's per-scene display persona on the <c>member</c> edge. <c>NotFound</c> if missing.</summary>
	Task<OneOf<SceneMember, NotFound>> SetShowAsAsync(string sceneId, string playerDbref, string showAs);

	// ── Plots ─────────────────────────────────────────────────────────────────

	/// <summary>Creates a plot (null id) or updates an existing one.</summary>
	Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref);

	/// <summary>Returns a plot by id, or <c>NotFound</c>.</summary>
	Task<OneOf<ScenePlot, NotFound>> GetPlotAsync(string plotId);

	/// <summary>Links a scene into a plot. <c>NotFound</c> if either is missing.</summary>
	Task<OneOf<None, NotFound>> LinkSceneToPlotAsync(string plotId, string sceneId);

	/// <summary>Unlinks a scene from a plot. <c>NotFound</c> if either is missing.</summary>
	Task<OneOf<None, NotFound>> UnlinkSceneFromPlotAsync(string plotId, string sceneId);

	// ── Derived reads ───────────────────────────────────────────────────────────

	/// <summary>Returns the distinct opaque tags present across a scene's poses. <c>NotFound</c> if missing.</summary>
	Task<OneOf<IReadOnlyList<string>, NotFound>> GetTagsAsync(string sceneId);

	/// <summary>Returns the distinct display personas (<c>ShowAsName</c>s) used in a scene. <c>NotFound</c> if missing.</summary>
	Task<OneOf<IReadOnlyList<string>, NotFound>> GetCastAsync(string sceneId);
}
