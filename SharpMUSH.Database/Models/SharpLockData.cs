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
	/// Lock flags as an integer (flags enum value)
	/// </summary>
	public int Flags { get; init; } = 0;
}
