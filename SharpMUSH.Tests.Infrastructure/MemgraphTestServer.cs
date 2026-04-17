using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Testcontainer for Memgraph graph database.
/// Uses the Bolt protocol on port 7687.
/// Only starts the container when SHARPMUSH_DATABASE_PROVIDER is set to "memgraph".
/// </summary>
public class MemgraphTestServer : IAsyncInitializer, IAsyncDisposable
{
	private const int BoltPort = 7687;

	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	private IContainer? _instance;

	public IContainer Instance => _instance ??= new ContainerBuilder("memgraph/memgraph:3.8.1")
		.WithNetwork(DockerNetwork.Instance)
		.WithPortBinding(BoltPort, true)
		.WithCommand(
			"--bolt-num-workers=4",
			"--storage-mode=IN_MEMORY_TRANSACTIONAL",
			"--memory-limit=1024",
			"--log-level=WARNING")
		.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("You are running Memgraph"))
		.WithReuse(false)
		.Build();

	public string BoltUri => $"bolt://localhost:{Instance.GetMappedPublicPort(BoltPort)}";

	private static bool IsMemgraphEnabled =>
		string.Equals(
			Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"),
			"memgraph",
			StringComparison.OrdinalIgnoreCase);

	public async Task InitializeAsync()
	{
		if (IsMemgraphEnabled)
		{
			await Instance.StartAsync();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_instance is not null)
		{
			await _instance.DisposeAsync();
		}
	}
}
