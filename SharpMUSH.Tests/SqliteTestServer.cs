using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class SqliteTestServer : IAsyncInitializer, IAsyncDisposable
{
	private readonly string _databasePath;

	public SqliteTestServer()
	{
		// Create a unique database file for each test session
		_databasePath = Path.Combine(Path.GetTempPath(), $"sharpmush_test_{Guid.NewGuid()}.db");
	}

	public string GetConnectionString() => $"Data Source={_databasePath}";

	public Task InitializeAsync()
	{
		// SQLite doesn't need initialization, database is created on first connection
		return Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		// Clean up the database file
		try
		{
			if (File.Exists(_databasePath))
			{
				// Add a small delay to allow any locked connections to close
				await Task.Delay(100);
				File.Delete(_databasePath);
			}
		}
		catch (IOException)
		{
			// Ignore errors if file is still locked
		}
		GC.SuppressFinalize(this);
	}
}
