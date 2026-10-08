namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Restarts the game engine without dropping anyone's connection: the players are told, then this process stops
/// and its supervisor (Docker's restart policy, Kubernetes, systemd) starts it again. Client sockets live in the
/// connection server, so they stay open across it. Without a supervisor the engine simply stops.
/// </summary>
public interface IServerRestart
{
	/// <summary>Whether a restart is already under way.</summary>
	bool Pending { get; }

	/// <summary>
	/// Tells every connected player that <paramref name="requestedBy"/> is restarting the game, then stops the
	/// process a moment later, so the request that asked for it gets its answer. A second call while one is under
	/// way does nothing and returns false.
	/// </summary>
	ValueTask<bool> RestartAsync(string requestedBy);
}
