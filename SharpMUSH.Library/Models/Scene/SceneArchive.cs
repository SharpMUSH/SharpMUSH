namespace SharpMUSH.Library.Models.Scene;

/// <summary>
/// A scene session — a collection of messages set in a specific room,
/// with optional title and description for the archive.
/// </summary>
/// <param name="Id">Storage key. Empty string for unsaved scenes.</param>
/// <param name="Title">Short player-supplied title (e.g. "The Duel at Dawn").</param>
/// <param name="Description">Optional summary paragraph written by a player or auto-generated.</param>
/// <param name="RoomDbref">DBRef of the MUSH room where the scene takes place.</param>
/// <param name="RoomName">Display name of the room at the time the scene was opened.</param>
/// <param name="ParticipantDbrefs">DBRefs of every character who posted at least one message.</param>
/// <param name="StartedAt">UTC time the scene was opened.</param>
/// <param name="ClosedAt">UTC time the scene was closed, or null if still active.</param>
/// <param name="IsPublic">When true the scene is visible to anyone; when false it is player-only.</param>
public record SceneArchive(
	string Id,
	string Title,
	string Description,
	string RoomDbref,
	string RoomName,
	IReadOnlyList<string> ParticipantDbrefs,
	DateTimeOffset StartedAt,
	DateTimeOffset? ClosedAt,
	bool IsPublic);
