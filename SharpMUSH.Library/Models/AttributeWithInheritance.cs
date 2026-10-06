namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents the source of an inherited attribute.
/// </summary>
public enum AttributeSource
{
	/// <summary>
	/// Attribute is defined directly on the object.
	/// </summary>
	Self,

	/// <summary>
	/// Attribute is inherited from a parent in the parent chain.
	/// </summary>
	Parent,

	/// <summary>
	/// Attribute is inherited from the object's type ancestor (PennMUSH ANCESTOR_*) or one of the
	/// ancestor's own parents.
	/// </summary>
	Ancestor
}

/// <summary>
/// How far PennMUSH's <c>atr_get_with_parent</c> reaches past the object itself
/// (<c>src/attrib.c:1218-1270</c>).
/// </summary>
/// <param name="Ancestor">
/// The object's type ancestor (<c>Ancestor_Parent</c>, <c>hdrs/dbdefs.h:225-232</c>), or null when it has
/// none or is ORPHAN.
/// </param>
/// <param name="MaxParents">
/// <c>MAX_PARENTS</c>: the most objects one leg of the walk visits, counting the object it starts on
/// (<c>while (parent_depth &lt; MAX_PARENTS ...)</c>).
/// </param>
public readonly record struct InheritanceWalk(DBRef? Ancestor, int MaxParents)
{
	/// <summary>PennMUSH's default <c>max_parents</c>, and SharpMUSH's <c>Limit.MaxParents</c> default.</summary>
	public const int DefaultMaxParents = 10;

	/// <summary>The @parent chain alone, at the default depth.</summary>
	public static InheritanceWalk ParentsOnly => new(null, DefaultMaxParents);
}

/// <summary>
/// Represents an attribute along with its inheritance information.
/// This includes where the attribute was found (self, parent, or type ancestor),
/// and the flags adjusted for inheritance semantics.
/// </summary>
public record AttributeWithInheritance(
	/// <summary>
	/// The attribute path as an array of SharpAttribute objects.
	/// For simple attributes, this will be a single element.
	/// For nested attributes (e.g., FOO`BAR`BAZ), this contains the full path.
	/// </summary>
	SharpAttribute[] Attributes,

	/// <summary>
	/// The DBRef of the object where this attribute was actually found.
	/// </summary>
	DBRef SourceObject,

	/// <summary>
	/// Indicates whether the attribute comes from the object itself, a parent, or the type ancestor.
	/// </summary>
	AttributeSource Source,

	/// <summary>
	/// Flags adjusted for inheritance.
	/// Non-inheritable flags are filtered out when the attribute is inherited.
	/// </summary>
	IEnumerable<SharpAttributeFlag> InheritedFlags);

/// <summary>
/// Lazy version of AttributeWithInheritance for efficient retrieval.
/// </summary>
public record LazyAttributeWithInheritance(
	/// <summary>
	/// The attribute path as an array of LazySharpAttribute objects.
	/// </summary>
	LazySharpAttribute[] Attributes,

	/// <summary>
	/// The DBRef of the object where this attribute was actually found.
	/// </summary>
	DBRef SourceObject,

	/// <summary>
	/// Indicates whether the attribute comes from the object itself, a parent, or the type ancestor.
	/// </summary>
	AttributeSource Source,

	/// <summary>
	/// Flags adjusted for inheritance.
	/// Non-inheritable flags are filtered out when the attribute is inherited.
	/// </summary>
	IEnumerable<SharpAttributeFlag> InheritedFlags);
