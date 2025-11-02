using Testcontainers.ArangoDb;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class ArangoDbTestServer : IAsyncInitializer, IAsyncDisposable
{
	public ArangoDbContainer Instance { get; } =  new ArangoDbBuilder()
		.WithImage("arangodb:latest")
		.WithPassword("password")
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
}