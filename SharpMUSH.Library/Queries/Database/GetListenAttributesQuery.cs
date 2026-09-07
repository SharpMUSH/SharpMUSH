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
/// Results are cached automatically via QueryCachingBehavior, and invalidated by key: every
/// attribute-mutating command names this key through <see cref="Definitions.CacheKeys.AttributesTouchedBy"/>.
/// </summary>
/// <remarks>
/// KNOWN GAP: the handler walks the object's parent chain, so a write to a PARENT's attributes also
/// changes this answer, and nothing expires it — the entry is keyed by the child, and the write only
/// names the object it wrote. Closing it needs the read to record which objects it consulted, the
/// same thing <see cref="Definitions.CacheTags.InheritedAttributes"/> is waiting on; the two should
/// be fixed together. Until then this is stale for at most the entry duration after a parent edit.
/// </remarks>
public record GetListenAttributesQuery(AnySharpObject SharpObject) : IQuery<ListenAttributeCache[]>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.Listens(SharpObject.Object().DBRef);
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
