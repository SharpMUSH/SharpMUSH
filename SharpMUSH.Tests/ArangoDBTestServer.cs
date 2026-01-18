using Testcontainers.ArangoDb;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class ArangoDbTestServer : IAsyncInitializer, IAsyncDisposable
{
	private ArangoDbContainer? _instance;
	
	public ArangoDbContainer Instance => _instance ?? throw new InvalidOperationException("Container not initialized. Call InitializeAsync first.");

	public async Task InitializeAsync()
	{
		_instance = new ArangoDbBuilder("arangodb:latest")
			.WithName("sharpmush-test-arangodb")
			.WithLabel("reuse-id", "SharpMUSH")
			.WithLabel("reuse-hash", "sharpmush-arangodb-v1")
			.WithPassword("password")
			.WithReuse(true)
			.Build();
		await _instance.StartAsync();
	}
	
	public async ValueTask DisposeAsync()
	{
		if (_instance != null)
		{
			await _instance.DisposeAsync();
		}
	}
}