namespace SharpMUSH.Plugins.Scene.Contracts;

/// <summary>
/// A scene — the <c>node_sharp_sys_scene_scenes</c> vertex. References to game
/// objects (room/owner/starter) are graph edges to the real vertices; each
/// carries a <c>*Name</c> snapshot captured at the occurrence so a deleted or
/// renamed object still renders, and an optional <c>*Dbref</c> resolved from the
/// live edge (null when the object no longer exists). Lifecycle is the free-string
/// <see cref="Status"/>; descriptive/custom keys live in <see cref="Meta"/>.
/// </summary>
/// <param name="Id">Storage key. Empty string for unsaved scenes.</param>
/// <param name="Status">Free-string lifecycle state (defaults <c>new</c>/<c>active</c>/<c>paused</c>/<c>finished</c>).</param>
/// <param name="IsPublic">When true the scene is visible to anyone; when false it is member-only.</param>
/// <param name="IsTempRoom">Informational; set by softcode when it dug a temp room for the scene.</param>
/// <param name="ScheduledFor">UTC Unix-millis the scene is scheduled for, or null when not scheduled.</param>
/// <param name="StartedAt">UTC Unix-millis the scene was created.</param>
/// <param name="LastActivityAt">UTC Unix-millis of the most recent mutation (drives recency ordering).</param>
/// <param name="PoseCount">Denormalized count of non-deleted poses.</param>
/// <param name="OwnerDbref">Live owner dbref resolved from the owner edge, or null if the object is gone.</param>
/// <param name="OwnerName">Snapshot of the owner's name at the time it was set.</param>
/// <param name="StarterDbref">Live starter dbref resolved from the starter edge, or null if the object is gone.</param>
/// <param name="StarterName">Snapshot of the starter's name.</param>
/// <param name="RoomDbref">Live room dbref resolved from the in-room edge, or null (roomless scheduled scene, or room gone).</param>
/// <param name="RoomName">Snapshot of the room's name when the scene was bound to it.</param>
/// <param name="Meta">Opaque key/value bag for descriptive + custom metadata (title, summary, icdate, location, type, warning, …).</param>
public record Scene(
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
