using MySqlConnector;
using SharpMUSH.Library.Services.Interfaces;
using System.Data.Common;

namespace SharpMUSH.Library.Services.Providers;

/// <summary>
/// MySQL/MariaDB implementation of the SQL provider
/// </summary>
public class MySqlProvider : ISqlProvider
{
	private readonly MySqlDataSource _dataSource;

	public MySqlProvider(string connectionString)
	{
		_dataSource = new MySqlDataSource(connectionString);
	}

	public bool IsAvailable => !string.IsNullOrEmpty(_dataSource.ConnectionString);

	public string ProviderName => "MySQL";

	public string ParameterPlaceholderFormat => "?";

	public async ValueTask<DbConnection> CreateConnectionAsync()
		=> await _dataSource.OpenConnectionAsync();

	public string Escape(string value)
		=> MySqlHelper.EscapeString(value);

	public async ValueTask DisposeAsync()
	{
		await _dataSource.DisposeAsync();
		GC.SuppressFinalize(this);
	}
}
