using System.Text.Json;
using Dapper;
using MySqlConnector;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// MySQL implementation of the SQL service
/// </summary>
public class MySqlService(MySqlDataSource source) : ISqlService
{
	public bool IsAvailable => !string.IsNullOrEmpty(source.ConnectionString);

	public async ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query)
	{
		await using var connection = await source.OpenConnectionAsync();
		var result = await connection.QueryAsync<dynamic>(query);

		return result.Select<dynamic, Dictionary<string, object?>>(x =>
			JsonSerializer.Deserialize<Dictionary<string, object?>>(
				JsonSerializer.Serialize(x)));
	}

	public async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamQueryAsync(string query)
	{
		await using var connection = await source.OpenConnectionAsync();
		var result = connection.QueryUnbufferedAsync(query);

		await foreach (var row in result)
		{
			yield return JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(row));
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
		=> MySqlHelper.EscapeString(value);
}
