using System.Data.Common;
using Npgsql;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services.Providers;

/// <summary>
/// PostgreSQL implementation of the SQL provider
/// </summary>
public class PostgreSqlProvider : ISqlProvider
{
	private readonly NpgsqlDataSource _dataSource;

	public PostgreSqlProvider(string connectionString)
	{
		_dataSource = NpgsqlDataSource.Create(connectionString);
	}

	public bool IsAvailable => !string.IsNullOrEmpty(_dataSource.ConnectionString);

	public string ProviderName => "PostgreSQL";

	public async ValueTask<DbConnection> CreateConnectionAsync()
		=> await _dataSource.OpenConnectionAsync();

	/// <summary>
	/// Escapes a string for PostgreSQL. Single quotes are escaped by doubling them.
	/// Note: This provides basic escaping. Parameterized queries are preferred for SQL injection prevention.
	/// </summary>
	public string Escape(string value)
	{
		// PostgreSQL uses a different escaping mechanism
		// Single quotes are escaped by doubling them
		return value.Replace("'", "''");
	}

	public async ValueTask DisposeAsync()
	{
		await _dataSource.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}
