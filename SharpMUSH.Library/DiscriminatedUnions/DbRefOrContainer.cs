using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of DBRef and AnySharpContainer, replacing OneOf&lt;DBRef, AnySharpContainer&gt;.
/// Used in content and exit queries.
/// </summary>
public union DbRefOrContainer(DBRef, AnySharpContainer)
{
	public bool IsDBRef     => Value is DBRef;
	public bool IsContainer => Value is AnySharpContainer;

	public DBRef           AsDBRef     => (DBRef)Value!;
	public AnySharpContainer AsContainer => (AnySharpContainer)Value!;
}
