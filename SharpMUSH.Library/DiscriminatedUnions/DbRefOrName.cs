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
}
