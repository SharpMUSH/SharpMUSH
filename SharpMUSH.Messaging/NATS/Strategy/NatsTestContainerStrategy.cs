using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Strategy that starts a NATS server in a Testcontainer.
/// Used for local development when <c>NATS_URL</c> is not set —
/// no external NATS installation is required.
/// </summary>
public sealed class NatsTestContainerStrategy : NatsStrategy
{
	private const int NatsPort = 4222;

	/// <summary>
	/// The log message emitted by NATS when it is ready to accept connections.
	/// Used as the container readiness signal. Valid for nats:2-alpine.
	/// </summary>
	private const string NatsReadyMessage = "Server is ready";

	private IContainer? _container;

	public override async ValueTask<string> GetUrlAsync()
	{
		if (_container is null)
		{
			_container = new ContainerBuilder("nats:2-alpine")
				.WithPortBinding(NatsPort, true)   // random host port to avoid collisions
				.WithCommand("-js")                // enable JetStream
				.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(NatsReadyMessage))
				.WithLabel("reuse-id", "SharpMUSH-NATS")
				.WithReuse(true)                   // shared across Server and ConnectionServer processes
				.Build();

			await _container.StartAsync();
		}

		var port = _container.GetMappedPublicPort(NatsPort);
		return $"nats://localhost:{port}";
	}

	public override ValueTask DisposeAsync()
	{
		// Reused containers are intentionally kept alive by Testcontainers so that
		// the sibling process (Server or ConnectionServer) can continue using them.
		// Testcontainers will not stop a container that was started with WithReuse(true).
		_container = null;
		return ValueTask.CompletedTask;
	}
}
