namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Strategy that connects to an already-running NATS server.
/// Used in Docker Compose, Kubernetes, and other externally-managed environments
/// where <c>NATS_URL</c> is provided.
/// </summary>
public sealed class NatsExternalStrategy(string natsUrl) : NatsStrategy
{
	public override ValueTask<string> GetUrlAsync() => ValueTask.FromResult(natsUrl);

	public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
