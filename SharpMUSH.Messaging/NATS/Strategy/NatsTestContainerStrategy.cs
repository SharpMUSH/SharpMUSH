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
	/// Maximum message payload size sent to the NATS server via <c>--max_payload</c>.
	/// </summary>
	private const int MaxPayloadBytes = 6 * 1024 * 1024; // 6 MB

	private NatsContainer? _container;

	public override async ValueTask<string> GetUrlAsync()
	{
		if (_container is null)
		{
			_container = new NatsBuilder(NatsImage)
				.WithCommand("--max_payload", MaxPayloadBytes.ToString())
				.WithLabel("reuse-id", "SharpMUSH-NATS")
				.WithReuse(true)   // shared across Server and ConnectionServer processes
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
