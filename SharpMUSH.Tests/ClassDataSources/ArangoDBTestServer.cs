using Testcontainers.ArangoDb;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

public class ArangoDbTestServer : IAsyncInitializer
{
	private static readonly Lazy<ArangoDbContainer> _container = new(() => 
		new ArangoDbBuilder("arangodb:latest")
			.WithName("sharpmush-test-arangodb")
			.WithLabel("reuse-id", "SharpMUSH")
			.WithLabel("reuse-hash", "sharpmush-arangodb-v1")
			.WithPassword("password")
			.WithReuse(true)
			.Build());
	
	private static bool _initialized;
	private static readonly object _lock = new();

	public ArangoDbContainer Instance => _container.Value;

	public async Task InitializeAsync()
	{
		// Ensure container is only started once across all test sessions
		lock (_lock)
		{
			if (_initialized) return;
			_initialized = true;
		}
		
		await Instance.StartAsync();
		
		// Set test-specific environment variable for ArangoDB connection
		var connectionString = $"Server=http://localhost:{Instance.GetMappedPublicPort(8529)};Database=_system;User=root;Password=password";
		Environment.SetEnvironmentVariable("ARANGO_TEST_CONNECTION_STRING", connectionString);
		
		// Enable fast migration mode (disables WaitForSync, enables batching, suppresses migration logging)
		Environment.SetEnvironmentVariable("SHARPMUSH_FAST_MIGRATION", "true");
		
		// Disable configuration file reloading to avoid inotify file descriptor exhaustion
		// With 32 parallel tests, each WebApplication creates file watchers that exhaust the system limit (1280)
		Environment.SetEnvironmentVariable("DOTNET_hostBuilder:reloadConfigOnChange", "false");
	}
}