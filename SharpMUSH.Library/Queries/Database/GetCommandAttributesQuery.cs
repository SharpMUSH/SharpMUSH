using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to get command attributes for an object with pre-compiled regex patterns.
/// Results are cached automatically via QueryCachingBehavior.
/// </summary>
public record GetCommandAttributesQuery(AnySharpObject SharpObject) : IQuery<CommandAttributeCache[]>, ICacheable
{
	public string CacheKey => $"commands:{SharpObject.Object().DBRef}";
	public string[] CacheTags => [];
}

/// <summary>
/// Cache entry for command attributes with pre-compiled regex patterns.
/// </summary>
public record CommandAttributeCache(
	SharpAttribute Attribute,
	Regex CompiledRegex,
	bool IsRegexFlag);
