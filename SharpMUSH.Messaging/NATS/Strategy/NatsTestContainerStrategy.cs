using System.Text;
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
	private const string NatsConfigPath = "/etc/nats/nats.conf";
	private const int MaxPayloadBytes = 6 * 1024 * 1024; // 6 MB
	private static readonly byte[] NatsConfig = Encoding.UTF8.GetBytes($"max_payload: {MaxPayloadBytes}\njetstream: true\n");

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
				.WithResourceMapping(NatsConfig, NatsConfigPath) // Embed config with max_payload and JetStream
				.WithCommand("-c", NatsConfigPath)               // Load config file
				.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(NatsReadyMessage))
				.WithLabel("reuse-id", "SharpMUSH-NATS")
				.WithReuse(true)                   // shared across Server and ConnectionServer processes
				.Build();

			await _container.StartAsync();
		}

		var port = _container.GetMappedPublicPort(NatsPort);
		return $"nats://localhost:{port}";
	}

	public override async ValueTask DisposeAsync()
	{
		if (_container is not null)
		{
			await _container.StopAsync();
			await _container.DisposeAsync();
			_container = null;
		}
	}
}
