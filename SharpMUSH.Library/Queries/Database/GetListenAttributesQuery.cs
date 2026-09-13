using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Visible local listen patterns with cached compiled expressions. This query does not traverse parents;
/// inherited searches use complete <see cref="GetListenAttributeSnapshotQuery"/> snapshots so ordinary
/// definitions and private trees can participate in shadowing.
/// </summary>
/// <remarks>
/// Attribute mutations invalidate the inherited-attributes tag. Compiled expressions are reused through
/// <see cref="Utilities.SoftcodeRegex"/> after a local snapshot is rebuilt.
/// </remarks>
public record GetListenAttributesQuery(AnySharpObject SharpObject) : IQuery<ListenAttributeCache[]>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.Listens(SharpObject.Object().DBRef);
	public string[] CacheTags => [Definitions.CacheTags.InheritedAttributes];
}

/// <summary>
/// Cache entry for listen attributes with pre-compiled regex patterns.
/// </summary>
public record ListenAttributeCache(
	SharpAttribute Attribute,
	Regex CompiledRegex,
	bool IsRegexFlag,
	ListenBehavior Behavior);
