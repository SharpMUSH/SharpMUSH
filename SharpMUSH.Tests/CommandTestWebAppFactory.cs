namespace SharpMUSH.Tests;

/// <summary>
/// WebAppFactory configured for command tests with isolated database
/// </summary>
public class CommandTestWebAppFactory : WebAppFactory
{
	private const string DatabaseName = "sharpmush_test_commands";

	public CommandTestWebAppFactory() : base(null, DatabaseName, "mysql")
	{
	}

	public override async Task InitializeAsync()
	{
		// Create the database before initializing the app factory
		await using var connection = new MySqlConnector.MySqlConnection(MySqlTestServer.Instance.GetConnectionString());
		await connection.OpenAsync();
		await using (var cmd = new MySqlConnector.MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{DatabaseName}`", connection))
		{
			await cmd.ExecuteNonQueryAsync();
		}
		
		// Call base with updated connection string
		// We need to use reflection to set the connection string because it's readonly
		var field = typeof(WebAppFactory).GetField("_customSqlConnectionString", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		field?.SetValue(this, MySqlTestServer.GetConnectionString(DatabaseName));
		
		await base.InitializeAsync();
	}
}
