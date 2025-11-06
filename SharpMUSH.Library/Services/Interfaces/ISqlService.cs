using System.Collections.Generic;
using System.Threading.Tasks;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for executing SQL queries against the configured database
/// </summary>
public interface ISqlService
{
	/// <summary>
	/// Executes a SQL query and returns the results as a list of rows
	/// </summary>
	/// <param name="query">The SQL query to execute</param>
	/// <returns>A list of rows, where each row is a dictionary of column names to values</returns>
	ValueTask<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string query);
	
	/// <summary>
	/// Executes a SQL query and returns the results as a list of rows
	/// </summary>
	/// <param name="query">The SQL query to execute</param>
	/// <returns>A list of rows, where each row is a dictionary of column names to values</returns>
	IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamQueryAsync(string query);

	/// <summary>
	/// Executes a SQL query and returns a formatted string result
	/// </summary>
	/// <param name="query">The SQL query to execute</param>
	/// <param name="delimiter">The delimiter to use between values (default: space)</param>
	/// <returns>A formatted string of results</returns>
	ValueTask<string> ExecuteQueryAsStringAsync(string query, string delimiter = " ");

	/// <summary>
	/// Escapes a string for safe use in SQL queries
	/// </summary>
	/// <param name="value">The value to escape</param>
	/// <returns>The escaped value</returns>
	string Escape(string value);

	/// <summary>
	/// Checks if SQL support is enabled and configured
	/// </summary>
	bool IsAvailable { get; }
}
