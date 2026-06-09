using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Scene;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for creating, querying, and archiving scenes.
/// A scene is an ordered log of <see cref="SceneMessage"/> lines
/// produced during a role-play session in a specific room.
/// </summary>
/// <remarks>
/// Methods that might not find a resource return <c>OneOf&lt;T, NotFound&gt;</c>.
/// Methods that can fail due to invalid state return <c>OneOf&lt;T, Error&lt;string&gt;&gt;</c>.
/// </remarks>
public interface ISceneService
{
	// ── Archive / session management ─────────────────────────────────────────

	/// <summary>
	/// Opens a new scene in the given room and returns its ID.
	/// </summary>
	Task<SceneArchive> OpenSceneAsync(
		string roomDbref,
		string roomName,
		string title = "",
		bool isPublic = true);

	/// <summary>
	/// Closes an active scene, stamping <see cref="SceneArchive.ClosedAt"/>.
	/// Returns <c>NotFound</c> if the scene ID does not exist.
	/// </summary>
	Task<OneOf<SceneArchive, NotFound>> CloseSceneAsync(string sceneId);

	/// <summary>
	/// Updates the title or description of an existing scene.
	/// Returns <c>NotFound</c> if the scene ID does not exist.
	/// </summary>
	Task<OneOf<SceneArchive, NotFound>> UpdateSceneMetaAsync(
		string sceneId,
		string? title = null,
		string? description = null);

	/// <summary>
	/// Returns a scene archive record by ID.
	/// Returns <c>NotFound</c> if the scene ID does not exist.
	/// </summary>
	Task<OneOf<SceneArchive, NotFound>> GetSceneAsync(string sceneId);

	/// <summary>
	/// Returns recently closed scenes, ordered by <see cref="SceneArchive.ClosedAt"/> descending.
	/// </summary>
	Task<IReadOnlyList<SceneArchive>> GetRecentScenesAsync(int count = 20);

	/// <summary>
	/// Returns currently active (open) scenes, ordered by <see cref="SceneArchive.StartedAt"/> descending.
	/// </summary>
	Task<IReadOnlyList<SceneArchive>> GetActiveScenesAsync();

	// ── Message operations ────────────────────────────────────────────────────

	/// <summary>
	/// Appends a message to an open scene.
	/// Returns <c>NotFound</c> if the scene does not exist.
	/// Returns <c>Error&lt;string&gt;</c> if the scene is already closed.
	/// </summary>
	Task<OneOf<SceneMessage, NotFound, Error<string>>> PostMessageAsync(
		string sceneId,
		string authorDbref,
		string authorName,
		string plainContent,
		string renderedHtml,
		SceneMessageType messageType = SceneMessageType.Pose);

	/// <summary>
	/// Returns all messages for a scene, ordered by <see cref="SceneMessage.Timestamp"/> ascending.
	/// Returns <c>NotFound</c> if the scene does not exist.
	/// </summary>
	Task<OneOf<IReadOnlyList<SceneMessage>, NotFound>> GetMessagesAsync(string sceneId);

	/// <summary>
	/// Returns the most recent <paramref name="count"/> messages for a scene,
	/// ordered by timestamp ascending (oldest first within the window).
	/// Returns <c>NotFound</c> if the scene does not exist.
	/// </summary>
	Task<OneOf<IReadOnlyList<SceneMessage>, NotFound>> GetRecentMessagesAsync(
		string sceneId,
		int count = 50);
}
