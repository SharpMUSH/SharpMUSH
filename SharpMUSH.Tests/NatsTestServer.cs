using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Testcontainer for a NATS server with JetStream enabled.
/// Exposes NATS client port (4222) on a random host port.
/// </summary>
public class NatsTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const int NatsPort = 4222;
	private const string NatsConfigPath = "/etc/nats/nats.conf";
	private const int MaxPayloadBytes = 6 * 1024 * 1024; // 6 MB
	private static readonly byte[] NatsConfig = Encoding.UTF8.GetBytes($"max_payload: {MaxPayloadBytes}\njetstream: true\n");

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public IContainer Instance => field ??= new ContainerBuilder("nats:2-alpine")
		.WithNetwork(DockerNetwork.Instance)
		.WithPortBinding(NatsPort, true) // Random host port
		.WithResourceMapping(NatsConfig, NatsConfigPath) // Embed config with max_payload and JetStream
		.WithCommand("-c", NatsConfigPath)               // Load config file
		.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
