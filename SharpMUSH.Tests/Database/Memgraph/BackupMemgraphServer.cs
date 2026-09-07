using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Neo4j.Driver;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.Database.Memgraph;

/// <summary>
/// A Memgraph instance owned by the backup tests alone.
///
/// <para>Deliberately not the session-shared <c>MemgraphTestServer</c>: proving a backup restores
/// means emptying the graph and putting the capture back, and doing that to the database the rest of
/// the suite is running against would break whatever else is mid-test. An isolated instance is the
/// only honest way to test a destructive operation.</para>
///
/// <para>Started only on the memgraph leg of the matrix, matching how the shared server gates
/// itself — on any other leg these tests return early and this never runs.</para>
/// </summary>
public sealed class BackupMemgraphServer : IAsyncInitializer, IAsyncDisposable
{
	private const int BoltPort = 7687;

	private IContainer? _instance;
	private IDriver? _driver;

	private static bool MemgraphSelected =>
		string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"), "memgraph",
			StringComparison.OrdinalIgnoreCase);

	public async Task InitializeAsync()
	{
		if (!MemgraphSelected) return;

		_instance = new ContainerBuilder("memgraph/memgraph:3.8.1")
			.WithPortBinding(BoltPort, true)
			.WithCommand(
				"--bolt-num-workers=4",
				"--storage-mode=IN_MEMORY_TRANSACTIONAL",
				"--memory-limit=1024",
				"--log-level=WARNING")
			// Memgraph prints its banner before Bolt is accepting, so this alone hands back a container
			// that resets the next connection; the retry loop below is what actually makes it ready.
			.WithWaitStrategy(Wait.ForUnixContainer()
				.UntilMessageIsLogged("You are running Memgraph"))
			.WithReuse(false)
			.Build();

		await _instance.StartAsync();

		_driver = GraphDatabase.Driver(
			$"bolt://localhost:{_instance.GetMappedPublicPort(BoltPort)}",
			o => o.WithEncryptionLevel(EncryptionLevel.None));

		// And then verify the protocol, retrying: an open port still refuses a Bolt handshake for a
		// moment after it opens. Failing here would surface as a connection reset inside a test and say
		// nothing about the code under test, so the wait belongs in the fixture, not in the assertions.
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
		while (true)
		{
			try
			{
				await _driver.VerifyConnectivityAsync();
				return;
			}
			catch (Neo4jException) when (DateTime.UtcNow < deadline)
			{
				await Task.Delay(250);
			}
			catch (IOException) when (DateTime.UtcNow < deadline)
			{
				await Task.Delay(250);
			}
		}
	}

	/// <summary>The driver, or null on any leg that is not running Memgraph.</summary>
	public IDriver? Driver => _driver;

	public async ValueTask DisposeAsync()
	{
		if (_driver is not null) await _driver.DisposeAsync();
		if (_instance is null) return;
		try
		{
			await _instance.DisposeAsync();
		}
		catch
		{
			// Podman can fail the teardown when the network is already gone; the container is leaving
			// either way and a noisy dispose would mask the test result.
		}
	}
}
