using SharpMUSH.Library.Services;

namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents lock data including the lock string and its flags
/// </summary>
public record SharpLockData
{
	/// <summary>
	/// The lock expression/string (e.g., "=#123:456" or "flag^wizard")
	/// </summary>
	public string LockString { get; init; } = "#TRUE";
	
	/// <summary>
	/// Lock flags (visual, no_inherit, no_clone, wizard, owner, locked, etc.)
	/// </summary>
	public LockService.LockFlags Flags { get; init; } = LockService.LockFlags.Default;
	
	/// <summary>
	/// Default constructor for object initializer syntax
	/// </summary>
	public SharpLockData()
	{
	}
	
	/// <summary>
	/// Creates a SharpLockData with just a lock string and default flags
	/// </summary>
	public SharpLockData(string lockString)
	{
		LockString = lockString;
		Flags = LockService.LockFlags.Default;
	}
	
	/// <summary>
	/// Creates a SharpLockData with a lock string and specific flags
	/// </summary>
	public SharpLockData(string lockString, LockService.LockFlags flags)
	{
		LockString = lockString;
		Flags = flags;
	}
}
