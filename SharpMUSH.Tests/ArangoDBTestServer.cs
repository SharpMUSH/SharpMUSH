using Testcontainers.ArangoDb;
using SharpMUSH.Library.Definitions;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class ArangoDbTestServer : IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	private ArangoDbContainer? _instance;

	public ArangoDbContainer Instance => _instance ??= new ArangoDbBuilder("arangodb:latest")
		.WithNetwork(DockerNetwork.Instance)
		.WithPassword("password")
		.WithReuse(false)
		.Build();

	private static bool IsArangoEnabled =>
		!DatabaseProviderSelector.TryResolve(
			Environment.GetEnvironmentVariable(DatabaseProviderSelector.EnvironmentVariableName),
			out var provider)
		|| provider == DatabaseProvider.ArangoDB;

	public async Task InitializeAsync()
	{
		if (IsArangoEnabled)
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