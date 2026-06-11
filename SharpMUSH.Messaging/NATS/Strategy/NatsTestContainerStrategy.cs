using System.Text;
using DotNet.Testcontainers.Builders;
using Testcontainers.Nats;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Strategy that starts a NATS server in a Testcontainer.
/// Used for local development when <c>NATS_URL</c> is not set —
/// no external NATS installation is required.
/// </summary>
public sealed class NatsTestContainerStrategy : NatsStrategy
{
	/// <summary>
	/// The NATS docker image to use. Pinned to a specific 2.x Alpine release.
	/// </summary>
	private const string NatsImage = "nats:2.14-alpine";

	/// <summary>
	/// Maximum message payload size configured via a NATS config file.
	/// </summary>
	private const int MaxPayloadBytes = 6 * 1024 * 1024; // 6 MB

	private const string NatsConfigPath = "/etc/nats/nats.conf";

	private static readonly byte[] NatsConfig = Encoding.UTF8.GetBytes(
		$"max_payload: {MaxPayloadBytes}\njetstream: true\n");

	private NatsContainer? _container;

	public override async ValueTask<string> GetUrlAsync()
	{
		if (_container is null)
		{
			_container = new NatsBuilder(NatsImage)
				.WithResourceMapping(NatsConfig, NatsConfigPath)
				.WithCommand("-c", NatsConfigPath)
				.WithLabel("reuse-id", "SharpMUSH-NATS")
				.WithReuse(true)   // shared across Server and ConnectionServer processes
				// Port-based readiness instead of the image default's log-message wait. A reused
				// container's startup log has already scrolled past, so replaying a log wait hangs
				// forever (notably under rootless podman); checking the client port is open succeeds
				// immediately whether the container is fresh or reused.
				.WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
				.Build();

			await _container.StartAsync();
		}

		return _container.GetConnectionString();
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
