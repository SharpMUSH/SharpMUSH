using Testcontainers.ArangoDb;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

public class ArangoDbTestServer : IAsyncInitializer, IAsyncDisposable
{
	public ArangoDbContainer Instance { get; } = new ArangoDbBuilder("arangodb:latest")
		.WithName("sharpmush-test-arangodb")
		.WithLabel("reuse-id", "SharpMUSH")
		.WithLabel("reuse-hash", "sharpmush-arangodb-v1")
		.WithPassword("password")
		.WithReuse(true)
		.Build();

	public async Task InitializeAsync()
	{
		await Instance.StartAsync();
		
		// Set test-specific environment variable for ArangoDB connection
		var connectionString = $"Server=http://localhost:{Instance.GetMappedPublicPort(8529)};Database=_system;User=root;Password=password";
		Environment.SetEnvironmentVariable("ARANGO_TEST_CONNECTION_STRING", connectionString);
	}

	public async ValueTask DisposeAsync()
	{
		await Instance.DisposeAsync();
	}
}