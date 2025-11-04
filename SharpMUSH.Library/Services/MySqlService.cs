using MySqlConnector;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// MySQL implementation of the SQL service
/// </summary>
public class MySqlService : ISqlService
{
	private readonly string _connectionString;

	public MySqlService(string connectionString)
	{
		_connectionString = connectionString;
	}

	public bool IsAvailable => !string.IsNullOrEmpty(_connectionString);

	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query)
	{
		var results = new List<Dictionary<string, object?>>();

		await using var connection = new MySqlConnection(_connectionString);
		await connection.OpenAsync();

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
		}

		return results;
	}

	public async IAsyncEnumerable<Dictionary<string, object?>> ExecuteQueryStreamAsync(string query)
	{
		await using var connection = new MySqlConnection(_connectionString);
		await connection.OpenAsync();

		await using var command = new MySqlCommand(query, connection);
		await using var reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			var row = new Dictionary<string, object?>();
			for (int i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			yield return row;
		}
	}

	public async ValueTask<string> ExecuteQueryAsStringAsync(string query, string delimiter = " ")
	{
		var results = await ExecuteQueryAsync(query);
		var rows = results.ToList();

		if (rows.Count == 0)
		{
			return string.Empty;
		}

		var output = new List<string>();
		
		foreach (var row in rows)
		{
			var values = row.Values.Select(v => v?.ToString() ?? string.Empty);
			output.Add(string.Join(delimiter, values));
		}

		return string.Join("\n", output);
	}

	public string Escape(string value) 
		=> MySqlHelper.EscapeString(value);
}
