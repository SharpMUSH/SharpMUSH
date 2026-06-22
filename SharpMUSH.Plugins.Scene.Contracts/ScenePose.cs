namespace SharpMUSH.Plugins.Scene.Contracts;

/// <summary>
/// A pose — the <c>node_sharp_sys_scene_poses</c> vertex. The pose is the ordered
/// <em>slot</em> (its position is the <c>pose_next</c> linked list, not an integer);
/// its text lives in versioned <see cref="ScenePoseEdit"/> vertices, of which
/// <see cref="Content"/>/<see cref="Markup"/> here project the current edit.
/// Author/origin are graph edges to real objects, each with a <c>*Name</c> snapshot
/// (display) and an optional resolved <c>*Dbref</c> (null if the object is gone).
/// <see cref="AuthorDbref"/> is always the controlling player (permission anchor);
/// <see cref="ShowAsName"/> is the display persona (blank = the author).
/// </summary>
/// <param name="Id">Storage key (globally unique; addressed independently of the scene).</param>
/// <param name="SceneId">Owning scene id (the <c>pose_in_scene</c> edge target).</param>
/// <param name="AuthorDbref">Live author dbref resolved from the author edge, or null if the object is gone.</param>
/// <param name="AuthorName">Snapshot of the real author's name at pose time (credit/audit).</param>
/// <param name="ShowAsName">Display persona snapshot at pose time; empty string = shown as the author.</param>
/// <param name="OriginDbref">Live origin-room dbref resolved from the origin edge, or null if gone.</param>
/// <param name="OriginName">Snapshot of the origin room's name at pose time.</param>
/// <param name="Source">Opaque origin label set by softcode (e.g. "pose", "say", "combat", "channel:Public").</param>
/// <param name="Tags">Arbitrary opaque labels for portal/softcode filtering. No hard-coded categories.</param>
/// <param name="Meta">Opaque key/value bag for custom per-pose metadata.</param>
/// <param name="CreatedAt">UTC Unix-millis the pose was added.</param>
/// <param name="IsDeleted">Soft-delete flag; the slot remains in the pose_next chain.</param>
/// <param name="Content">Plain text (ANSI-stripped) of the current edit, for search/accessibility.</param>
/// <param name="Markup">Raw MString markup of the current edit; rendered client-side (never pre-rendered HTML).</param>
/// <param name="EditCount">Number of content versions (drives an "edited" badge); 1 for an unedited pose.</param>
/// <param name="LastEditedAt">UTC Unix-millis of the most recent edit, or null if never edited after creation.</param>
/// <param name="LastEditorDbref">Live dbref of the most recent editor, or null if the object is gone / never edited.</param>
/// <param name="LastEditorName">Snapshot name of the most recent editor, or null if never edited.</param>
public record ScenePose(
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
