using Testcontainers.ArangoDb;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class ArangoDbTestServer : IAsyncInitializer, IAsyncDisposable
{
	public ArangoDbContainer Instance { get; } =  new ArangoDbBuilder("arangodb:latest")
		.WithPassword("password")
		.WithReuse(true)
		.WithLabel("testcontainers.reuse.hash", "sharpmush-arangodb-test")
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}