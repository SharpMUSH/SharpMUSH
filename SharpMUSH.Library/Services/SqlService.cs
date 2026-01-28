using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.Providers;

namespace SharpMUSH.Library.Services;

/// <summary>
/// SQL service implementation using the Strategy pattern to support multiple database providers.
/// Supports runtime configuration changes by creating providers on-demand.
/// </summary>
public class SqlService : ISqlService
{
	private readonly IOptionsMonitor<SharpMUSHOptions>? _config;
	private readonly ISqlProvider? _staticProvider;
	
	/// <summary>
	/// Primary constructor for production use. Supports runtime configuration changes.
	/// </summary>
	public SqlService(IOptionsMonitor<SharpMUSHOptions> config)
	{
		_config = config;
	}
	
	/// <summary>
	/// Testing constructor that creates a static provider with fixed connection string.
	/// Used for unit tests to avoid dependency on configuration system.
	/// </summary>
	public SqlService(string connectionString, string platform = "mysql")
	{
		var platformLower = platform.ToLowerInvariant();
		
		_staticProvider = platformLower switch
		{
			"mysql" or "mariadb" => new MySqlProvider(connectionString),
			"postgresql" or "postgres" => new PostgreSqlProvider(connectionString),
			"sqlite" => new SqliteProvider(connectionString),
			_ => throw new NotSupportedException($"SQL platform '{platform}' is not supported. Supported platforms: mysql, postgresql, sqlite")
		};
	}

	public bool IsAvailable => GetCurrentProvider()?.IsAvailable ?? false;
	
	/// <summary>
	/// Gets the current provider, creating it from current configuration if needed.
	/// This allows the service to respond to runtime configuration changes.
	/// </summary>
	private ISqlProvider? GetCurrentProvider()
	{
		// If using static provider (testing), return it
		if (_staticProvider != null)
		{
			return _staticProvider;
		}
		
		// Create provider from current configuration
		if (_config == null)
		{
			return null;
		}
		
		var cvn = _config.CurrentValue.Net;
		
		// Return null if no SQL configuration
		if (string.IsNullOrWhiteSpace(cvn.SqlHost) && string.IsNullOrWhiteSpace(cvn.SqlDatabase))
		{
			return null;
		}

		var platform = cvn.SqlPlatform?.ToLowerInvariant() ?? "mysql";
		
		return platform switch
		{
			"mysql" or "mariadb" => new MySqlProvider(
				$"Server={cvn.SqlHost};Uid={cvn.SqlUsername};Pwd={cvn.SqlPassword};Database={cvn.SqlDatabase}"),
			"postgresql" or "postgres" => new PostgreSqlProvider(
				$"Host={cvn.SqlHost};Username={cvn.SqlUsername};Password={cvn.SqlPassword};Database={cvn.SqlDatabase}"),
			"sqlite" => new SqliteProvider(
				$"Data Source={cvn.SqlDatabase}"),
			_ => throw new NotSupportedException($"SQL platform '{platform}' is not supported. Supported platforms: mysql, postgresql, sqlite")
		};
	}
	
	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query)
	{
		var provider = GetCurrentProvider();
		if (provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}

		var results = new List<Dictionary<string, object?>>();

		await using var connection = await provider.CreateConnectionAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = query;
		await using var reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			var row = new Dictionary<string, object?>();
			for (var i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			results.Add(row);
		}

		return results;
	}

	public async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamQueryAsync(string query)
	{
		var provider = GetCurrentProvider();
		if (provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}

		await using var connection = await provider.CreateConnectionAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = query;
		await using var reader = await command.ExecuteReaderAsync();
		
		while (await reader.ReadAsync(CancellationToken.None))
		{
			var row = new Dictionary<string, object?>();
			for (var i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			yield return row;
		}
	}

	public async ValueTask<string> ExecuteQueryAsStringAsync(string query, string delimiter = " ")
	{
		var results = await ExecuteQueryAsync(query);

		var output = results
			.Select(row => row.Values.Select(v => v?.ToString() ?? string.Empty))
			.Select(values => string.Join(delimiter, values))
			.ToArray();

		return string.Join("\n", output);
	}

	public string Escape(string value)
	{
		var provider = GetCurrentProvider();
		if (provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}
		
		return provider.Escape(value);
	}
}
