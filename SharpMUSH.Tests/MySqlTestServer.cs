using Testcontainers.MySql;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class MySqlTestServer : IAsyncInitializer, IAsyncDisposable
{
	public MySqlContainer Instance { get; } = new MySqlBuilder("mysql:latest")
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
