using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// The time zone a client reported for its connection: telnet NEW-ENVIRON's <c>TZ</c>, or the web portal's browser
/// zone. <c>clientzone()</c> and <c>@sockset</c> read it; what to do with it is softcode's choice.
/// </summary>
public static class ConnectionTimeZone
{
	public const string Key = "ClientTimeZone";

	private const int MaxLength = 64;

	/// <summary>
	/// The zone a client's text names, written as the server writes it, or <see cref="NotFound"/>. Only an IANA zone
	/// the server knows, in its own case, is one: a POSIX rule such as <c>CET-1CEST</c>, or a Windows name, is not. A leading <c>:</c>, which POSIX allows in <c>TZ</c>, is dropped.
	/// </summary>
	public static Found<string> Parse(string? reported)
	{
		var name = (reported ?? string.Empty).Trim().TrimStart(':');
		if (name.Length is 0 or > MaxLength || name.Any(char.IsControl)) return new NotFound();

		// TryFindSystemTimeZoneById caches a zone under a key that ignores case, so an earlier lookup of Asia/Kolkata
		// would let asia/kolkata through; the id is compared as written.
		return TimeZoneInfo.TryFindSystemTimeZoneById(name, out var zone)
			&& zone.HasIanaId
			&& string.Equals(zone.Id, name, StringComparison.Ordinal)
				? zone.Id
				: new NotFound();
	}
}
