using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Basic implementation of SQL service that currently returns not implemented
/// This is a stub for future SQL integration
/// </summary>
public class SqlService : ISqlService
{
	private readonly MySqlService? _mySql = null;
	
	public SqlService(IOptionsMonitor<SharpMUSHOptions> config)
	{
		// TODO: Support multiple database types.
		var cvn = config.CurrentValue.Net;
		var connectionString = $"Server={cvn.SqlHost};Uid={cvn.SqlUsername};Pwd={cvn.SqlPassword};Database={cvn.SqlDatabase}";

		_mySql = new MySqlService(new MySqlDataSource(connectionString));
	}
	
	public SqlService(string connectionString)
	{
		_mySql = new MySqlService(new MySqlDataSource(connectionString));
	}

	public bool IsAvailable => _mySql?.IsAvailable ?? false;
	
	public ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query)
		=> _mySql!.ExecuteQueryAsync(query);

	public IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamQueryAsync(string query) 
		=> _mySql!.ExecuteStreamQueryAsync(query);

	public ValueTask<string> ExecuteQueryAsStringAsync(string query, string delimiter = " ")
		=> _mySql!.ExecuteQueryAsStringAsync(query, delimiter);

	public string Escape(string value) 
		=> _mySql!.Escape(value);
}
