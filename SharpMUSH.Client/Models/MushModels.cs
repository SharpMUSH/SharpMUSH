namespace SharpMUSH.Client.Models;

/// <summary>
/// How the portal writes an object's reference. An account's attribute holder travels as its reserved number
/// (int.MinValue plus the account's key) and is written <c>#acct:&lt;key&gt;</c>.
/// </summary>
public static class MushRefs
{
	/// <summary>Every number below this is an account holder's.</summary>
	public const int AccountCeiling = -(1 << 30);

	public static bool IsAccount(int dbref) => dbref < AccountCeiling;

	public static int Account(int key) => int.MinValue + key;

	public static string Format(int dbref) => IsAccount(dbref) ? $"#acct:{dbref - int.MinValue}" : $"#{dbref}";
}

public class MushObject
{
	public int Dbref { get; set; }
	public string Name { get; set; } = string.Empty;
	public MushObjectType Type { get; set; }
	public string Flags { get; set; } = string.Empty;
	public string Owner { get; set; } = string.Empty;
	public List<MushAttribute> Attributes { get; set; } = [];
}

public class MushAttribute
{
	public string Name { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
	public List<string> AttributeFlags { get; set; } = [];

	/// <summary>The attribute's own read key, or null when it has none or the reader may not examine the object.</summary>
	public string? ReadKey { get; set; }

	/// <summary>The attribute's own write key, or null when it has none or the reader may not examine the object.</summary>
	public string? WriteKey { get; set; }
}

public enum MushObjectType
{
	Thing,
	Room,
	Exit,
	Player,
	Unknown,
	Account
}

public class MushSearchResult
{
	public int Dbref { get; set; }
	public string Name { get; set; } = string.Empty;
	public MushObjectType Type { get; set; }
}
