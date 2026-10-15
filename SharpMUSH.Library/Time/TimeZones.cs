namespace SharpMUSH.Library.Time;

/// <summary>
/// Finds a time zone by the name softcode gave, the same way whatever was looked up before.
/// </summary>
public static class TimeZones
{
	/// <summary>
	/// The zone <paramref name="name"/> names, matched by case as PennMUSH's zone files are.
	/// </summary>
	/// <remarks>
	/// <see cref="TimeZoneInfo.TryFindSystemTimeZoneById"/> reads a zone from disk by its exact name, then keeps it
	/// in a cache that ignores case. So <c>asia/kolkata</c> is no zone until <c>Asia/Kolkata</c> has been read, and
	/// is that zone (with <c>Asia/Kolkata</c> as its id) afterwards. A name that differs from the zone's id only by
	/// case is refused, so the answer never depends on what ran first. A name that converts to a zone of another id,
	/// such as a Windows name, is unaffected.
	/// </remarks>
	public static bool TryFind(string name, out TimeZoneInfo zone)
	{
		if (TimeZoneInfo.TryFindSystemTimeZoneById(name, out var found)
			&& (found.Id == name || !found.Id.Equals(name, StringComparison.OrdinalIgnoreCase)))
		{
			zone = found;
			return true;
		}

		zone = TimeZoneInfo.Utc;
		return false;
	}
}
