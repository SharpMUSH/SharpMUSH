using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of DBRef and AnySharpObject, replacing OneOf&lt;DBRef, AnySharpObject&gt;.
/// Used in nearby-objects and zone queries.
/// </summary>
public union DbRefOrObject(DBRef, AnySharpObject)
{
	public bool IsDBRef  => Value is DBRef;
	public bool IsObject => Value is AnySharpObject;

	public DBRef         AsDBRef  => (DBRef)Value!;
	public AnySharpObject AsObject => (AnySharpObject)Value!;
}
