using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to get an attribute with full inheritance chain resolution in a single database call.
/// This query searches for an attribute following the inheritance order:
/// 1. The object itself
/// 2. Parent chain (parent, grandparent, etc.) - if checkParent is true
/// 3. Object's zone chains - if checkParent is true
/// 4. Parent's zone chains - if checkParent is true
/// 5. Grandparent's zone chains - if checkParent is true
/// ... and so on for the entire hierarchy
/// 
/// IMPORTANT: Parents take precedence over zones at all levels.
/// 
/// Returns the complete attribute path (FOO → BAR → BAZ) from the first object in the inheritance chain where the attribute is found.
/// Each attribute in the path is returned as a separate AttributeWithInheritance instance with inherited flags merged.
/// </summary>
/// <param name="DBRef">The DBRef of the object to start the search from</param>
/// <param name="Attribute">The attribute path to search for (e.g., ["FOO"] or ["FOO", "BAR", "BAZ"])</param>
/// <param name="CheckParent">Whether to check parent and zone inheritance chains</param>
public record GetAttributeWithInheritanceQuery(
	DBRef DBRef,
	string[] Attribute,
	bool CheckParent = true)
	: IStreamQuery<AttributeWithInheritance>, ICacheable
{
	public string CacheKey => $"attribute-inheritance:{DBRef}:{string.Join("`", Attribute)}:{CheckParent}";

	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}

/// <summary>
/// Lazy version of GetAttributeWithInheritanceQuery for efficient retrieval.
/// Returns the complete attribute path (FOO → BAR → BAZ) from the first object in the inheritance chain where the attribute is found.
/// </summary>
public record GetLazyAttributeWithInheritanceQuery(
	DBRef DBRef,
	string[] Attribute,
	bool CheckParent = true)
	: IStreamQuery<LazyAttributeWithInheritance>, ICacheable
{
	public string CacheKey => $"lazy-attribute-inheritance:{DBRef}:{string.Join("`", Attribute)}:{CheckParent}";

	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}
