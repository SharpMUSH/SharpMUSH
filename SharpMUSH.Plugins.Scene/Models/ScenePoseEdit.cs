namespace SharpMUSH.Plugins.Scene.Models;

/// <summary>
/// A single content version of a pose — the <c>node_sharp_sys_scene_pose_edits</c>
/// vertex. A pose owns a chain of edits (<c>first_edit</c> + <c>next_edit</c>) and a
/// <c>current_edit</c> pointer; undo/redo move the pointer rather than mutating text.
/// The editor is a graph edge to the real player plus an <see cref="EditorName"/>
/// snapshot.
/// </summary>
/// <param name="Id">Storage key.</param>
/// <param name="PoseId">The pose this edit belongs to (the owning slot).</param>
/// <param name="Content">Plain text (ANSI-stripped) of this version.</param>
/// <param name="Markup">Raw MString markup of this version.</param>
/// <param name="EditorDbref">Live editor dbref resolved from the editor edge, or null if the object is gone.</param>
/// <param name="EditorName">Snapshot of the editor's name at the time of this edit.</param>
/// <param name="EditedAt">UTC Unix-millis this version was written.</param>
public record ScenePoseEdit(
	string Id,
	string PoseId,
	string Content,
	string Markup,
	string? EditorDbref,
	string EditorName,
	long EditedAt);
