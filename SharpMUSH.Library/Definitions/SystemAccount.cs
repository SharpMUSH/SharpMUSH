namespace SharpMUSH.Library.Definitions;

/// <summary>
/// The reserved account that owns server-authored content. It exists unconditionally, so anything
/// asking "has a human registered yet?" must exclude reserved accounts rather than counting rows.
/// </summary>
public static class SystemAccount
{
	public const string Username = "system";

	public static bool IsReserved(string username)
		=> string.Equals(username, Username, StringComparison.OrdinalIgnoreCase);
}
