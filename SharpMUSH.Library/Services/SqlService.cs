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
	private readonly IOptionsMonitor<SharpMUSHOptions> _config;
	
	/// <summary>
	/// Constructor that accepts configuration monitor for runtime configuration changes.
	/// </summary>
	public SqlService(IOptionsMonitor<SharpMUSHOptions> config)
	{
		_config = config;
	}

	public bool IsAvailable => GetCurrentProvider()?.IsAvailable ?? false;
	
	/// <summary>
	/// Gets the current provider, creating it from current configuration if needed.
	/// This allows the service to respond to runtime configuration changes.
	/// </summary>
	private ISqlProvider? GetCurrentProvider()
	{
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
				$"Server={cvn.SqlHost};User Id={cvn.SqlUsername};Password={cvn.SqlPassword};Database={cvn.SqlDatabase}"),
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
