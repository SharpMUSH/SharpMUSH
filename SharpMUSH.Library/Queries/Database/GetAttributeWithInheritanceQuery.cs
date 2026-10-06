using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// PennMUSH's <c>atr_get_with_parent</c>: an attribute resolved through the object, its <c>@parent</c>
/// chain and its type ancestor, in a single database call. See
/// <see cref="IAttributeStore.GetAttributeWithInheritanceAsync"/> for the walk.
/// </summary>
/// <param name="DBRef">The DBRef of the object to start the search from</param>
/// <param name="Attribute">The attribute path to search for (e.g., ["FOO"] or ["FOO", "BAR", "BAZ"])</param>
/// <param name="CheckParent">Whether to look past the object itself</param>
/// <param name="Walk">The type ancestor and depth bound; null is <see cref="InheritanceWalk.ParentsOnly"/>.</param>
/// <remarks>
/// The answer also depends on the attribute-entry table, which supplies the standard name an alias or
/// prefix means, so an entry change expires it too.
/// </remarks>
public record GetAttributeWithInheritanceQuery(
	DBRef DBRef,
	string[] Attribute,
	bool CheckParent = true,
	InheritanceWalk? Walk = null)
	: IStreamQuery<AttributeWithInheritance>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.AttributeWithInheritance(DBRef, Attribute, CheckParent, Walk);

	public string[] CacheTags => [Definitions.CacheTags.InheritedAttributes, Definitions.CacheTags.AttributeEntry];
}

/// <summary>
/// Lazy version of <see cref="GetAttributeWithInheritanceQuery"/>: the same walk, with values loaded on demand.
/// </summary>
public record GetLazyAttributeWithInheritanceQuery(
	DBRef DBRef,
	string[] Attribute,
	bool CheckParent = true,
	InheritanceWalk? Walk = null)
	: IStreamQuery<LazyAttributeWithInheritance>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.LazyAttributeWithInheritance(DBRef, Attribute, CheckParent, Walk);

	public string[] CacheTags => [Definitions.CacheTags.InheritedAttributes, Definitions.CacheTags.AttributeEntry];
}
