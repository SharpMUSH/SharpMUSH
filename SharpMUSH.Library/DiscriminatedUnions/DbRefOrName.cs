using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of DBRef and string name, replacing OneOf&lt;DBRef, string&gt; and OneOf&lt;string, DBRef&gt;.
/// Used in name-list helpers, communication targets, and scheduler task descriptors.
/// </summary>
public union DbRefOrName(DBRef, string)
{
	public bool   IsDBRef => Value is DBRef;
	public bool   IsName  => Value is string;
	public DBRef  AsDBRef => (DBRef)Value!;
	public string AsName  => (string)Value!;

	// Backward-compat aliases
	public bool   IsT0 => IsDBRef;
	public bool   IsT1 => IsName;
	public DBRef  AsT0 => AsDBRef;
	public string AsT1 => AsName;

	// Extra compat aliases for scripts that replaced IsT0→IsSuccess, AsT0→AsValue()
	public bool   IsSuccess => IsDBRef;
	public DBRef  AsValue()  => AsDBRef;
}
