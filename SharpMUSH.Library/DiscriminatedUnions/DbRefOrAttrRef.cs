using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of <see cref="DbRefAttribute"/> (object + attribute) and <see cref="DBRef"/> (object only).
/// Returned by <c>HelperFunctions.SplitDbRefAndOptionalAttr</c>.
/// Use <see cref="IsDbRefAttribute"/> to test whether an attribute was specified.
/// </summary>
public union DbRefOrAttrRef(DbRefAttribute, DBRef)
{
	public bool IsDbRefAttribute => Value is DbRefAttribute;
	public bool IsDBRef          => Value is DBRef;

	public DbRefAttribute AsDbRefAttribute => (DbRefAttribute)Value!;
	public DBRef          AsDBRef          => (DBRef)Value!;

	/// <summary>Gets the <see cref="DBRef"/> regardless of which case is active.</summary>
	public DBRef DbRef => Value switch
	{
		DbRefAttribute a => a.DbRef,
		DBRef d          => d,
		_                => throw new InvalidOperationException()
	};

	/// <summary>
	/// Gets the attribute name as a backtick-joined string when an attribute was specified;
	/// otherwise <c>null</c>.
	/// </summary>
	public string? Attribute => Value is DbRefAttribute a ? string.Join("`", a.Attribute) : null;

	/// <summary>
	/// Gets the attribute name segments when an attribute was specified; otherwise <c>null</c>.
	/// </summary>
	public string[]? AttributeArray => Value is DbRefAttribute a ? a.Attribute : null;
}
