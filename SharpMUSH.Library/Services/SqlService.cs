using System.Data.Common;
using SharpMUSH.Library.ParserInterfaces;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
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
	private readonly Lazy<IEventService>? _events;
	private ConnectionState _state = ConnectionState.Unknown;
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

	/// <summary>
	/// As <see cref="SqlService(IOptionsMonitor{SharpMUSHOptions})"/>, raising PennMUSH's <c>SQL`CONNECT</c>,
	/// <c>SQL`CONNECTFAIL</c> and <c>SQL`DISCONNECT</c>.
	/// </summary>
	public SqlService(IOptionsMonitor<SharpMUSHOptions> config, Lazy<IEventService> events)
	{
		_config = config;
		_events = events;
	}

	private enum ConnectionState { Unknown, Connected, Failed }

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

		if (string.IsNullOrWhiteSpace(cvn.SqlHost) && string.IsNullOrWhiteSpace(cvn.SqlDatabase))
		{
			return null;
		}

		var platform = cvn.SqlPlatform?.ToLowerInvariant() ?? "mysql";

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

		if (_currentProvider == null || _currentConnectionString != connectionString)
		{
			await _providerLock.WaitAsync(ExecutionBudget.CurrentToken);
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
				parts.Add($"Server={host[..colonIndex]}");
				parts.Add($"Port={host[(colonIndex + 1)..]}");
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
				parts.Add($"Host={host[..colonIndex]}");
				parts.Add($"Port={host[(colonIndex + 1)..]}");
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

		await using var connection = await OpenAsync(provider);
		await using var command = connection.CreateCommand();
		if (ExecutionBudget.Current is { } budget)
		{
			budget.ThrowIfExceeded();
			command.CommandTimeout = Math.Min(command.CommandTimeout > 0 ? command.CommandTimeout : int.MaxValue,
				Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(budget.Remaining.TotalSeconds))));
		}
		command.CommandText = query;
		await using var reader = await command.ExecuteReaderAsync(ExecutionBudget.CurrentToken);
		ExecutionBudget.Current?.ThrowIfExceeded();

		while (await reader.ReadAsync(ExecutionBudget.CurrentToken))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			var row = new Dictionary<string, object?>();
			for (var i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			results.Add(row);
		}

		ExecutionBudget.Current?.ThrowIfExceeded();
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

		await using var connection = await OpenAsync(provider);
		await using var command = connection.CreateCommand();
		if (ExecutionBudget.Current is { } budget)
		{
			budget.ThrowIfExceeded();
			command.CommandTimeout = Math.Min(command.CommandTimeout > 0 ? command.CommandTimeout : int.MaxValue,
				Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(budget.Remaining.TotalSeconds))));
		}
		command.CommandText = query;

		for (var i = 0; i < parameters.Length; i++)
		{
			var param = command.CreateParameter();

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
				param.ParameterName = $"@p{i}";
			}

			param.Value = parameters[i] ?? DBNull.Value;
			command.Parameters.Add(param);
		}

		await using var reader = await command.ExecuteReaderAsync(ExecutionBudget.CurrentToken);
		ExecutionBudget.Current?.ThrowIfExceeded();

		while (await reader.ReadAsync(ExecutionBudget.CurrentToken))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			var row = new Dictionary<string, object?>();
			for (var i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			results.Add(row);
		}

		ExecutionBudget.Current?.ThrowIfExceeded();
		return results;
	}

	public async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamQueryAsync(string query)
	{
		var provider = await GetCurrentProviderAsync();
		if (provider == null)
		{
			throw new InvalidOperationException("SQL provider is not configured");
		}

		await using var connection = await OpenAsync(provider);
		await using var command = connection.CreateCommand();
		if (ExecutionBudget.Current is { } budget)
		{
			budget.ThrowIfExceeded();
			command.CommandTimeout = Math.Min(command.CommandTimeout > 0 ? command.CommandTimeout : int.MaxValue,
				Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(budget.Remaining.TotalSeconds))));
		}
		command.CommandText = query;
		await using var reader = await command.ExecuteReaderAsync(ExecutionBudget.CurrentToken);
		ExecutionBudget.Current?.ThrowIfExceeded();

		while (await reader.ReadAsync(ExecutionBudget.CurrentToken))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
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

		await using var connection = await OpenAsync(provider);
		await using var command = connection.CreateCommand();
		if (ExecutionBudget.Current is { } budget)
		{
			budget.ThrowIfExceeded();
			command.CommandTimeout = Math.Min(command.CommandTimeout > 0 ? command.CommandTimeout : int.MaxValue,
				Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(budget.Remaining.TotalSeconds))));
		}
		command.CommandText = query;

		for (var i = 0; i < parameters.Length; i++)
		{
			var param = command.CreateParameter();

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
				param.ParameterName = $"@p{i}";
			}

			param.Value = parameters[i] ?? DBNull.Value;
			command.Parameters.Add(param);
		}

		await using var reader = await command.ExecuteReaderAsync(ExecutionBudget.CurrentToken);
		ExecutionBudget.Current?.ThrowIfExceeded();

		while (await reader.ReadAsync(ExecutionBudget.CurrentToken))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			var row = new Dictionary<string, object?>();
			for (var i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			yield return row;
		}
	}

	/// <summary>
	/// Opens a connection, raising <c>SQL`CONNECT</c> when one opens after none had, <c>SQL`DISCONNECT</c> when one
	/// fails after one had opened, and <c>SQL`CONNECTFAIL</c> when one fails otherwise (PennMUSH's src/sql.c).
	/// </summary>
	private async ValueTask<DbConnection> OpenAsync(ISqlProvider provider)
	{
		DbConnection connection;
		try
		{
			connection = await provider.CreateConnectionAsync();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			var previous = _state;
			_state = ConnectionState.Failed;
			await RaiseAsync(ex.Message, disconnected: previous == ConnectionState.Connected);
			throw;
		}

		if (_state != ConnectionState.Connected)
		{
			_state = ConnectionState.Connected;
			await RaiseAsync(null, disconnected: false);
		}

		return connection;
	}

	private async ValueTask RaiseAsync(string? error, bool disconnected)
	{
		if (_events is null)
		{
			return;
		}

		var platform = (_config.CurrentValue.Net.SqlPlatform?.ToLowerInvariant() ?? "mysql") switch
		{
			"postgresql" or "postgres" => "postgresql",
			"sqlite" => "sqlite3",
			_ => "mysql"
		};
		if (error is null)
		{
			await _events.Value.TriggerEventAsync(SharpEvents.SqlConnect, null, platform);
		}
		else if (disconnected)
		{
			await _events.Value.TriggerEventAsync(SharpEvents.SqlDisconnect, null, platform, error);
		}
		else
		{
			await _events.Value.TriggerEventAsync(SharpEvents.SqlConnectFail, null, platform, error);
		}
	}

	public async ValueTask<string> ExecuteQueryAsStringAsync(string query, string delimiter = " ")
		=> JoinRows(await ExecuteQueryAsync(query), delimiter);

	public async ValueTask<string> ExecutePreparedQueryAsStringAsync(string query, string delimiter = " ", params object?[] parameters)
		=> JoinRows(await ExecutePreparedQueryAsync(query, parameters), delimiter);

	private static string JoinRows(IEnumerable<Dictionary<string, object?>> rows, string delimiter)
		=> string.Join('\n', rows.Select(row => string.Join(delimiter, row.Values.Select(v => v?.ToString() ?? string.Empty))));

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
