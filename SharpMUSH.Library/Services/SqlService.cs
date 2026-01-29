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
				BuildMySqlConnectionString(cvn.SqlHost, cvn.SqlUsername, cvn.SqlPassword, cvn.SqlDatabase),
			"postgresql" or "postgres" => 
				BuildPostgreSqlConnectionString(cvn.SqlHost, cvn.SqlUsername, cvn.SqlPassword, cvn.SqlDatabase),
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
	
	private static string BuildMySqlConnectionString(string? host, string? username, string? password, string? database)
	{
		var parts = new List<string>();
		
		// Handle host:port format - MySQL needs separate Server and Port parameters
		if (!string.IsNullOrWhiteSpace(host))
		{
			var colonIndex = host.IndexOf(':');
			if (colonIndex > 0)
			{
				var serverPart = host.Substring(0, colonIndex);
				var portPart = host.Substring(colonIndex + 1);
				parts.Add($"Server={serverPart}");
				parts.Add($"Port={portPart}");
			}
			else
			{
				parts.Add($"Server={host}");
			}
		}
		
		if (!string.IsNullOrWhiteSpace(username))
			parts.Add($"User Id={username}");
		if (!string.IsNullOrWhiteSpace(password))
			parts.Add($"Password={password}");
		if (!string.IsNullOrWhiteSpace(database))
			parts.Add($"Database={database}");
			
		// Ensure we have at least a server to connect to
		if (parts.Count == 0 || string.IsNullOrWhiteSpace(host))
		{
			throw new InvalidOperationException("MySQL connection string must include at minimum a server/host");
		}
			
		return string.Join(";", parts);
	}
	
	private static string BuildPostgreSqlConnectionString(string? host, string? username, string? password, string? database)
	{
		var parts = new List<string>();
		
		// Handle host:port format - PostgreSQL needs separate Host and Port parameters
		if (!string.IsNullOrWhiteSpace(host))
		{
			var colonIndex = host.IndexOf(':');
			if (colonIndex > 0)
			{
				var serverPart = host.Substring(0, colonIndex);
				var portPart = host.Substring(colonIndex + 1);
				parts.Add($"Host={serverPart}");
				parts.Add($"Port={portPart}");
			}
			else
			{
				parts.Add($"Host={host}");
			}
		}
		
		if (!string.IsNullOrWhiteSpace(username))
			parts.Add($"Username={username}");
		if (!string.IsNullOrWhiteSpace(password))
			parts.Add($"Password={password}");
		if (!string.IsNullOrWhiteSpace(database))
			parts.Add($"Database={database}");
		return string.Join(";", parts);
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
	
	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecutePreparedQueryAsync(string query, params object?[] parameters)
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
		
		// Add parameters to the command
		for (var i = 0; i < parameters.Length; i++)
		{
			var param = command.CreateParameter();
			
			// Set parameter name based on provider type
			if (provider.ParameterPlaceholderFormat == "$")
			{
				// PostgreSQL uses numbered parameters like $1, $2, etc.
				param.ParameterName = $"${i + 1}";
			}
			else if (provider.ParameterPlaceholderFormat == "?")
			{
				// MySQL and SQLite use positional ? placeholders
				param.ParameterName = $"@p{i}";
			}
			else
			{
				// Default to named parameters
				param.ParameterName = $"@p{i}";
			}
			
			param.Value = parameters[i] ?? DBNull.Value;
			command.Parameters.Add(param);
		}
		
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
	
	public async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamPreparedQueryAsync(string query, params object?[] parameters)
	{
		var provider = await GetCurrentProviderAsync();
		if (provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}

		await using var connection = await provider.CreateConnectionAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = query;
		
		// Add parameters to the command
		for (var i = 0; i < parameters.Length; i++)
		{
			var param = command.CreateParameter();
			
			// Set parameter name based on provider type
			if (provider.ParameterPlaceholderFormat == "$")
			{
				// PostgreSQL uses numbered parameters like $1, $2, etc.
				param.ParameterName = $"${i + 1}";
			}
			else if (provider.ParameterPlaceholderFormat == "?")
			{
				// MySQL and SQLite use positional ? placeholders
				param.ParameterName = $"@p{i}";
			}
			else
			{
				// Default to named parameters
				param.ParameterName = $"@p{i}";
			}
			
			param.Value = parameters[i] ?? DBNull.Value;
			command.Parameters.Add(param);
		}
		
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
	
	public async ValueTask<string> ExecutePreparedQueryAsStringAsync(string query, string delimiter = " ", params object?[] parameters)
	{
		var results = await ExecutePreparedQueryAsync(query, parameters);

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
