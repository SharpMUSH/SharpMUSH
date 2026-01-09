using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to get listen pattern attributes for an object with pre-compiled regex patterns.
/// Results are cached automatically via QueryCachingBehavior.
/// Cache invalidated automatically via cache key when attribute commands execute.
/// </summary>
public record GetListenAttributesQuery(AnySharpObject SharpObject) : IQuery<ListenAttributeCache[]>, ICacheable
{
	public string CacheKey => $"listens:{SharpObject.Object().DBRef}";
	public string[] CacheTags => [];
}

/// <summary>
/// Cache entry for listen attributes with pre-compiled regex patterns.
/// </summary>
public record ListenAttributeCache(
	SharpAttribute Attribute,
	Regex CompiledRegex,
	bool IsRegexFlag,
	ListenBehavior Behavior);
