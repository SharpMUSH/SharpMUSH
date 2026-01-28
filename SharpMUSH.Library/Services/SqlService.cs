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
public class SqlService : ISqlService, IAsyncDisposable
{
	private readonly IOptionsMonitor<SharpMUSHOptions> _config;
	private readonly SemaphoreSlim _providerLock = new(1, 1);
	private ISqlProvider? _currentProvider;
	private string? _currentConnectionString;
	private bool _disposed;
	
	/// <summary>
	/// Constructor that accepts configuration monitor for runtime configuration changes.
	/// </summary>
	public SqlService(IOptionsMonitor<SharpMUSHOptions> config)
	{
		_config = config;
	}

	public bool IsAvailable => GetCurrentProviderAsync().GetAwaiter().GetResult()?.IsAvailable ?? false;
	
	/// <summary>
	/// Gets the current provider, creating it from current configuration if needed.
	/// This allows the service to respond to runtime configuration changes.
	/// Caches the provider and only recreates it if the connection string changes.
	/// Thread-safe implementation using semaphore.
	/// </summary>
	private async ValueTask<ISqlProvider?> GetCurrentProviderAsync()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(SqlService));
		}

		var cvn = _config.CurrentValue.Net;
		
		// Return null if no SQL configuration
		if (string.IsNullOrWhiteSpace(cvn.SqlHost) && string.IsNullOrWhiteSpace(cvn.SqlDatabase))
		{
			return null;
		}

		var platform = cvn.SqlPlatform?.ToLowerInvariant() ?? "mysql";
		
		// Build connection string to check if it has changed
		var connectionString = platform switch
		{
			"mysql" or "mariadb" => 
				$"Server={cvn.SqlHost};User Id={cvn.SqlUsername};Password={cvn.SqlPassword};Database={cvn.SqlDatabase}",
			"postgresql" or "postgres" => 
				$"Host={cvn.SqlHost};Username={cvn.SqlUsername};Password={cvn.SqlPassword};Database={cvn.SqlDatabase}",
			"sqlite" => 
				$"Data Source={cvn.SqlDatabase}",
			_ => throw new NotSupportedException($"SQL platform '{platform}' is not supported. Supported platforms: mysql, postgresql, sqlite")
		};
		
		// If connection string changed, dispose old provider and create new one
		if (_currentProvider == null || _currentConnectionString != connectionString)
		{
			await _providerLock.WaitAsync();
			try
			{
				// Double-check after acquiring lock
				if (_currentProvider == null || _currentConnectionString != connectionString)
				{
					var oldProvider = _currentProvider;
					
					_currentProvider = platform switch
					{
						"mysql" or "mariadb" => new MySqlProvider(connectionString),
						"postgresql" or "postgres" => new PostgreSqlProvider(connectionString),
						"sqlite" => new SqliteProvider(connectionString),
						_ => throw new NotSupportedException($"SQL platform '{platform}' is not supported. Supported platforms: mysql, postgresql, sqlite")
					};
					
					_currentConnectionString = connectionString;
					
					// Dispose old provider after setting new one
					if (oldProvider != null)
					{
						await oldProvider.DisposeAsync();
					}
				}
			}
			finally
			{
				_providerLock.Release();
			}
		}
		
		return _currentProvider;
	}
	
	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query)
	{
		var provider = await GetCurrentProviderAsync();
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
		var provider = await GetCurrentProviderAsync();
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
		var provider = GetCurrentProviderAsync().GetAwaiter().GetResult();
		if (provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}
		
		return provider.Escape(value);
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		
		if (_currentProvider != null)
		{
			await _currentProvider.DisposeAsync();
			_currentProvider = null;
		}
		
		_providerLock.Dispose();
		GC.SuppressFinalize(this);
	}
}
