using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// The game-wide server state document.
/// </summary>
public interface IServerStateStore
{
	/// <summary>
	/// Returns the game-wide server state document. Returns a default
	/// (SetupCompleted = false) if the document does not exist yet.
	/// </summary>
	ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default);

	/// <summary>Sets the game-wide SetupCompleted flag (upserts the state document).</summary>
	ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default);
}
