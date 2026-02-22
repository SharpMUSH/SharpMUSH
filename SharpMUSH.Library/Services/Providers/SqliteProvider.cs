using Microsoft.Data.Sqlite;
using SharpMUSH.Library.Services.Interfaces;
using System.Data.Common;

namespace SharpMUSH.Library.Services.Providers;

/// <summary>
/// SQLite implementation of the SQL provider
/// </summary>
public class SqliteProvider : ISqlProvider
{
	private readonly string _connectionString;

	public SqliteProvider(string connectionString)
	{
		_connectionString = connectionString;
	}

	public bool IsAvailable => !string.IsNullOrEmpty(_connectionString);

	public string ProviderName => "SQLite";

	public string ParameterPlaceholderFormat => "?";

	public async ValueTask<DbConnection> CreateConnectionAsync()
	{
		var connection = new SqliteConnection(_connectionString);
		await connection.OpenAsync();
		return connection;
	}

	/// <summary>
	/// Escapes a string for SQLite by doubling single quotes.
	/// Note: Microsoft.Data.Sqlite does not provide a built-in string literal escape function.
	/// The recommended approach is to use parameterized queries (SqliteParameter).
	/// This method provides basic escaping for legacy compatibility where parameterized queries cannot be used.
	/// </summary>
	public string Escape(string value)
	{
		// SQLite standard escaping: single quotes are escaped by doubling them
		// Microsoft.Data.Sqlite doesn't provide a library function for this
		// as it strongly encourages parameterized queries
		return value.Replace("'", "''");
	}

	public ValueTask DisposeAsync()
	{
		// SQLite provider doesn't hold any resources that need disposal
		GC.SuppressFinalize(this);
		return ValueTask.CompletedTask;
	}
}
