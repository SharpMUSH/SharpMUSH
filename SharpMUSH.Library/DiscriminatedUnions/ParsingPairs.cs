namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// Named replacement for the anonymous tuple (string db, string Attribute)
/// used in SplitObjectAndAttr return types.
/// </summary>
public readonly record struct ObjAttrPair(string Db, string Attribute);

/// <summary>
/// A union of ObjAttrPair (explicit object + attribute) and string (attribute only, object is implicit "self").
/// Returned by HelperFunctions.SplitOptionalObjectAndAttr.
/// Use Db to get the object specifier (null = self-relative), Attribute to get the attribute name.
/// </summary>
public union ObjAttrRef(ObjAttrPair, string)
{
	public bool IsExplicitObject => Value is ObjAttrPair;
	public bool IsSelfRelative   => Value is string;

	public ObjAttrPair AsObjAttrPair      => (ObjAttrPair)Value!;
	public string      AsAttributeString  => (string)Value!;

	/// <summary>Gets the object specifier, or <c>null</c> if self-relative.</summary>
	public string? Db => Value is ObjAttrPair p ? p.Db : null;

	/// <summary>Gets the attribute name regardless of which case is active.</summary>
	public string Attribute => Value switch
	{
		ObjAttrPair p => p.Attribute,
		string s      => s,
		_             => throw new InvalidOperationException()
	};
}
