using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class SharpObjectExtensions
{
	/// <summary>
	/// Check if an object has the NO_WARN flag set
	/// </summary>
	public static async Task<bool> HasNoWarnFlagAsync(this SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToListAsync();
		return flags.Any(x => x.Name == "NO_WARN");
	}
	
	/// <summary>
	/// Check if an object is marked as GOING (being destroyed)
	/// </summary>
	public static async Task<bool> IsGoingAsync(this SharpObject obj)
	{
		var flags = await obj.Flags.Value.ToListAsync();
		return flags.Any(x => x.Name == "GOING");
	}
}
