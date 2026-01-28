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
/// SQL service implementation using the Strategy pattern to support multiple database providers
/// </summary>
public class SqlService : ISqlService
{
	private readonly ISqlProvider? _provider = null;
	
	public SqlService(IOptionsMonitor<SharpMUSHOptions> config)
	{
		var cvn = config.CurrentValue.Net;
		
		// Return early if no SQL configuration
		if (string.IsNullOrWhiteSpace(cvn.SqlHost) && string.IsNullOrWhiteSpace(cvn.SqlDatabase))
		{
			return;
		}

		var platform = cvn.SqlPlatform?.ToLowerInvariant() ?? "mysql";
		
		_provider = platform switch
		{
			"mysql" or "mariadb" => new MySqlProvider(
				$"Server={cvn.SqlHost};Uid={cvn.SqlUsername};Pwd={cvn.SqlPassword};Database={cvn.SqlDatabase}"),
			"postgresql" or "postgres" => new PostgreSqlProvider(
				$"Host={cvn.SqlHost};Username={cvn.SqlUsername};Password={cvn.SqlPassword};Database={cvn.SqlDatabase}"),
			"sqlite" => new SqliteProvider(
				$"Data Source={cvn.SqlDatabase}"),
			_ => throw new NotSupportedException($"SQL platform '{cvn.SqlPlatform}' is not supported. Supported platforms: mysql, postgresql, sqlite")
		};
	}
	
	public SqlService(string connectionString, string platform = "mysql")
	{
		var platformLower = platform.ToLowerInvariant();
		
		_provider = platformLower switch
		{
			"mysql" or "mariadb" => new MySqlProvider(connectionString),
			"postgresql" or "postgres" => new PostgreSqlProvider(connectionString),
			"sqlite" => new SqliteProvider(connectionString),
			_ => throw new NotSupportedException($"SQL platform '{platform}' is not supported. Supported platforms: mysql, postgresql, sqlite")
		};
	}

	public bool IsAvailable => _provider?.IsAvailable ?? false;
	
	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query)
	{
		if (_provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}

		var guid = Guid.NewGuid();
		var results = new List<Dictionary<string, object?>>();

		await using var connection = await _provider.CreateConnectionAsync();
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
			Console.WriteLine($"{guid}: {JsonSerializer.Serialize(row)}");
		}

		return results;
	}

	public async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamQueryAsync(string query)
	{
		if (_provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}

		await using var connection = await _provider.CreateConnectionAsync();
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
		if (_provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}
		
		return _provider.Escape(value);
	}
}
