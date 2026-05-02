using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of <see cref="ObjAttrPair"/> (object + attribute) and <see cref="string"/> (object only).
/// Returned by <c>HelperFunctions.SplitDbRefAndOptionalAttr</c>.
/// The object specifier may be a DBRef (<c>#N</c>) or a name — both are passed as-is to the locate service.
/// Use <see cref="IsWithAttribute"/> to test whether an attribute was specified.
/// </summary>
public union DbRefOrAttrRef(ObjAttrPair, string)
{
	public bool IsWithAttribute => Value is ObjAttrPair;
	public bool IsObjectOnly    => Value is string;

	public ObjAttrPair AsObjAttrPair  => (ObjAttrPair)Value!;
	public string      AsObjectString => (string)Value!;

	/// <summary>Gets the raw object specifier (may be a name or a DBRef string such as <c>#5</c>).</summary>
	public string ObjSpecifier => Value switch
	{
		ObjAttrPair p => p.Db,
		string s      => s,
		_             => throw new InvalidOperationException()
	};

	/// <summary>
	/// Gets the attribute name when an attribute was specified; otherwise <c>null</c>.
	/// </summary>
	public string? Attribute => Value is ObjAttrPair p ? p.Attribute : null;

	/// <summary>
	/// Gets the attribute name segments when an attribute was specified; otherwise <c>null</c>.
	/// </summary>
	public string[]? AttributeArray => Value is ObjAttrPair p ? p.Attribute.Split("`") : null;
}
