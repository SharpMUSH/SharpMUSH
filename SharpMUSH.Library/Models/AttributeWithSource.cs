namespace SharpMUSH.Library.Models;

/// <summary>
/// A pattern match paired with the object it was actually found on.
/// <para>
/// PennMUSH's read gate re-walks an attribute's backtick-delimited ancestor path on every
/// access, and <c>can_read_attr_internal</c> (<c>src/attrib.c:318-356</c>) does that walk
/// against <c>target</c> - the object currently being examined while descending the parent
/// chain - not against the object the lookup started from. With <c>CheckParents</c> a match can
/// come from a parent several levels up, whose branch nodes have no counterpart on the child, so
/// the walk needs to know which object produced the match. That provenance is known while
/// <c>GetAttributesQueryHandler</c> iterates the chain and was previously thrown away, leaving
/// the read walk to query the child, find nothing, and grant on a path collapsed to the leaf
/// alone.
/// </para>
/// <para>
/// Deliberately a wrapper rather than a field on <see cref="SharpAttribute"/>: the source is a
/// property of THIS lookup (the same stored attribute is "self" to its owner and "parent" to
/// every child), not of the attribute, and <see cref="SharpAttribute"/> is the persisted model.
/// </para>
/// </summary>
/// <param name="Attribute">The matched attribute.</param>
/// <param name="SourceObject">The object the match was read from - the child, or a parent.</param>
public record AttributeWithSource(SharpAttribute Attribute, DBRef SourceObject);

/// <inheritdoc cref="AttributeWithSource"/>
public record LazyAttributeWithSource(LazySharpAttribute Attribute, DBRef SourceObject);
