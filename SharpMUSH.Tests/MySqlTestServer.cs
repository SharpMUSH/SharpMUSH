using Testcontainers.MySql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class MySqlTestServer : IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	public MySqlContainer Instance => field ??= new MySqlBuilder("mysql:latest")
		.WithNetwork(DockerNetwork.Instance)
		.WithDatabase("sharpmush_test")
		.WithUsername("testuser")
		.WithPassword("testpass")
		.WithReuse(false)
		.Build();

	public async Task InitializeAsync() => await Instance.StartAsync();
	public async ValueTask DisposeAsync() => await Instance.DisposeAsync();
	
	/// <summary>
	/// Gets a connection string for a specific database name. Creates the database if it doesn't exist.
	/// </summary>
	public string GetConnectionString(string databaseName)
	{
		var baseConnectionString = Instance.GetConnectionString();
		// Replace the database name in the connection string
		var builder = new MySqlConnector.MySqlConnectionStringBuilder(baseConnectionString)
		{
			Database = databaseName
		};
		return builder.ConnectionString;
	}
}
