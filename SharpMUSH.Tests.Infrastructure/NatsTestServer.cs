using System.Text;
using DotNet.Testcontainers.Containers;
using Testcontainers.Nats;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Testcontainer for a NATS server with JetStream enabled.
/// Exposes NATS client port (4222) on a random host port.
/// </summary>
public class NatsTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const string NatsImage = "nats:2.14-alpine";
	private const int MaxPayloadBytes = 6 * 1024 * 1024; // 6 MB
	private const string NatsConfigPath = "/etc/nats/nats.conf";
	private static readonly byte[] NatsConfig = Encoding.UTF8.GetBytes(
		$"max_payload: {MaxPayloadBytes}\njetstream: true\n");

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public IContainer Instance => field ??= new NatsBuilder(NatsImage)
		.WithNetwork(DockerNetwork.Instance)
		.WithResourceMapping(NatsConfig, NatsConfigPath)
		.WithCommand("-c", NatsConfigPath)
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}
