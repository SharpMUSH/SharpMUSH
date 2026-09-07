using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Neo4j.Driver;
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
		if (!IsMemgraphEnabled) return;

		await Instance.StartAsync();

		// Memgraph logs its banner — the readiness check above — before Bolt reliably accepts a
		// session, so a test connecting immediately after start can be met with a connection reset.
		// Retrying the handshake here holds the container until it is actually usable; without it the
		// whole memgraph leg fails on timing rather than on anything under test, intermittently.
		await using var driver = GraphDatabase.Driver(BoltUri, o => o.WithEncryptionLevel(EncryptionLevel.None));
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
		while (true)
		{
			try
			{
				await driver.VerifyConnectivityAsync();
				return;
			}
			catch (Exception ex) when ((ex is Neo4jException or IOException) && DateTime.UtcNow < deadline)
			{
				await Task.Delay(250);
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_instance is not null)
		{
			try
			{
				await _instance.StopAsync();
			}
			catch
			{
				// Podman may fail if the network was already removed
			}

			try
			{
				await _instance.DisposeAsync();
			}
			catch
			{
				// Podman may fail if the network was already removed
			}
		}
	}
}
