using Npgsql;
using SharpMUSH.Library.Services.Interfaces;
using System.Data.Common;

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

	public string ParameterPlaceholderFormat => "$";

	public async ValueTask<DbConnection> CreateConnectionAsync()
		=> await _dataSource.OpenConnectionAsync();

	/// <summary>
	/// Escapes a string for PostgreSQL by doubling single quotes.
	/// Note: Npgsql does not provide a built-in string literal escape function like MySqlHelper.EscapeString().
	/// The recommended approach is to use parameterized queries (NpgsqlParameter).
	/// This method provides basic escaping for legacy compatibility where parameterized queries cannot be used.
	/// </summary>
	public string Escape(string value)
	{
		// PostgreSQL standard escaping: single quotes are escaped by doubling them
		// This is equivalent to MySQL's quote escaping but PostgreSQL doesn't provide
		// a library function for this as it strongly encourages parameterized queries
		return value.Replace("'", "''");
	}

	public async ValueTask DisposeAsync()
	{
		await _dataSource.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}
