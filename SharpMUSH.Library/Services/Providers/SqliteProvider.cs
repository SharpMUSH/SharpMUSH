using System.Data.Common;
using Microsoft.Data.Sqlite;
using SharpMUSH.Library.Services.Interfaces;

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

	public async ValueTask<DbConnection> CreateConnectionAsync()
	{
		var connection = new SqliteConnection(_connectionString);
		await connection.OpenAsync();
		return connection;
	}

	public string Escape(string value)
	{
		// SQLite uses single quote escaping by doubling
		return value.Replace("'", "''");
	}
}
