namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Base strategy for obtaining a NATS server URL at startup.
/// Mirrors <c>ArangoStartupStrategy</c> — concrete implementations choose whether
/// to connect to an already-running server or to spin up a Testcontainer.
/// </summary>
public abstract class NatsStrategy : IAsyncDisposable
{
	/// <summary>
	/// Returns the NATS URL (e.g. "nats://localhost:4222") to connect to.
	/// Implementations may start a container as part of this call.
	/// </summary>
	public abstract ValueTask<string> GetUrlAsync();

	/// <summary>
	/// Performs cleanup when the application shuts down (e.g. stops a container).
	/// </summary>
	public abstract ValueTask DisposeAsync();
}
