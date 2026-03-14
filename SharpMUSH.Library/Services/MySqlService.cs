using MySqlConnector;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Library.Services;

/// <summary>
/// MySQL implementation of the SQL service
/// </summary>
public class MySqlService(MySqlDataSource source) : ISqlService
{
	public bool IsAvailable => !string.IsNullOrEmpty(source.ConnectionString);

	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query)
	{
		var guid = Guid.NewGuid();
		var results = new List<Dictionary<string, object?>>();

		await using var connection = await source.OpenConnectionAsync();
		await using var command = new MySqlCommand(query, connection);
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
		await using var connection = await source.OpenConnectionAsync();
		await using var command = new MySqlCommand(query, connection);
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

	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecutePreparedQueryAsync(string query, params object?[] parameters)
	{
		var guid = Guid.NewGuid();
		var results = new List<Dictionary<string, object?>>();

		await using var connection = await source.OpenConnectionAsync();
		await using var command = new MySqlCommand(query, connection);

		// Add parameters
		for (var i = 0; i < parameters.Length; i++)
		{
			command.Parameters.AddWithValue($"@p{i}", parameters[i] ?? DBNull.Value);
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
			Console.WriteLine($"{guid}: {JsonSerializer.Serialize(row)}");
		}

		return results;
	}

	public async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamPreparedQueryAsync(string query, params object?[] parameters)
	{
		await using var connection = await source.OpenConnectionAsync();
		await using var command = new MySqlCommand(query, connection);

		// Add parameters
		for (var i = 0; i < parameters.Length; i++)
		{
			command.Parameters.AddWithValue($"@p{i}", parameters[i] ?? DBNull.Value);
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
		=> MySqlHelper.EscapeString(value);
}
