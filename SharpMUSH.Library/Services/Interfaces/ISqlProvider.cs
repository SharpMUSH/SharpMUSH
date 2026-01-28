using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Strategy interface for SQL database providers
/// </summary>
public interface ISqlProvider
{
	/// <summary>
	/// Creates a database connection
	/// </summary>
	/// <returns>A new database connection</returns>
	ValueTask<DbConnection> CreateConnectionAsync();
	
	/// <summary>
	/// Escapes a string for safe use in SQL queries
	/// </summary>
	/// <param name="value">The value to escape</param>
	/// <returns>The escaped value</returns>
	string Escape(string value);

	/// <summary>
	/// Checks if the provider is available and configured
	/// </summary>
	bool IsAvailable { get; }
	
	/// <summary>
	/// Gets the name of the provider
	/// </summary>
	string ProviderName { get; }
}
