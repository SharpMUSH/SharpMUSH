namespace SharpMUSH.Database.Models;

/// <summary>
/// Database representation of lock data including the lock string and its flags
/// </summary>
public record SharpLockDataQueryResult
{
	/// <summary>
	/// The lock expression/string (e.g., "=#123:456" or "flag^wizard")
	/// </summary>
	public string LockString { get; init; } = "#TRUE";
	
	/// <summary>
	/// Lock flags as a string (e.g., "Visual|Private|NoClone")
	/// </summary>
	public string Flags { get; init; } = string.Empty;
}
