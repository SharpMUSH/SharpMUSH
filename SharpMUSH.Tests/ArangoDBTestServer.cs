using Testcontainers.ArangoDb;
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
		!string.Equals(
			Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"),
			"memgraph",
			StringComparison.OrdinalIgnoreCase);

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