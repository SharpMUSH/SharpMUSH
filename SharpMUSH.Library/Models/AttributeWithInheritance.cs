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
	/// Attribute is inherited from a zone.
	/// </summary>
	Zone
}

/// <summary>
/// Represents an attribute along with its inheritance information.
/// This includes where the attribute was found (self, parent, or zone),
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
	/// Indicates whether the attribute comes from the object itself, a parent, or a zone.
	/// </summary>
	AttributeSource Source,

	/// <summary>
	/// Flags adjusted for inheritance.
	/// Non-inheritable flags are filtered out when the attribute is inherited from a parent or zone.
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
	/// Indicates whether the attribute comes from the object itself, a parent, or a zone.
	/// </summary>
	AttributeSource Source,

	/// <summary>
	/// Flags adjusted for inheritance.
	/// Non-inheritable flags are filtered out when the attribute is inherited from a parent or zone.
	/// </summary>
	IEnumerable<SharpAttributeFlag> InheritedFlags);
