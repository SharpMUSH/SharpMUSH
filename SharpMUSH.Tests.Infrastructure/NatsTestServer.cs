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
	private static readonly string NatsConfigContent = $"max_payload: {MaxPayloadBytes}\njetstream: true\n";

	// Write config to a temp file for bind-mount (Podman rootless can't PUT /archive on stopped containers)
	private readonly string _configTempFile = CreateTempConfigFile();

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public IContainer Instance => field ??= new NatsBuilder(NatsImage)
		.WithNetwork(DockerNetwork.Instance)
		.WithBindMount(_configTempFile, NatsConfigPath)
		.WithCommand("-c", NatsConfigPath)
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();

	public async ValueTask DisposeAsync()
	{
		try
		{
			await Instance.StopAsync();
		}
		catch
		{
			// Podman may fail if the network was already removed
		}

		try
		{
			await Instance.DisposeAsync();
		}
		catch
		{
			// Podman may fail if the network was already removed
		}

		try
		{
			File.Delete(_configTempFile);
		}
		catch
		{
			// Best-effort cleanup of temp config file
		}
	}

	private static string CreateTempConfigFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"nats-{Guid.NewGuid():N}.conf");
		File.WriteAllText(path, NatsConfigContent);
		return path;
	}
}
