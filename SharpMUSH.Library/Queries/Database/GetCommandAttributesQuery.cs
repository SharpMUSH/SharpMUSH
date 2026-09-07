using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to get command attributes for an object with pre-compiled regex patterns.
/// Results are cached automatically via QueryCachingBehavior, and invalidated two ways: by key, which
/// every attribute-mutating command names through
/// <see cref="Definitions.CacheKeys.AttributesTouchedBy"/>, and by the
/// <see cref="Definitions.CacheTags.InheritedAttributes"/> tag.
/// </summary>
/// <remarks>
/// The tag is there because the handler walks the object's PARENT CHAIN: a write to an ancestor
/// changes this answer, and that write names only itself. Without it a builder adding a $-command to a
/// parent watched the children ignore it until the entry expired.
/// <para>
/// It is the game-wide tag, so any attribute write anywhere drops every object's set and they are
/// rebuilt on next use. That is the same over-expiry the inherited attribute reads carry and for the
/// same reason — what a read consulted is not recoverable from what it returned, and an ancestor that
/// contributed nothing still has to be able to start contributing. Rebuilding costs an attribute scan
/// over cached reads and no database round trip, and the compiled patterns survive in
/// <see cref="Utilities.SoftcodeRegex"/>. Scoping it needs the read to record the chain it walked;
/// see the note on <see cref="Definitions.CacheKeys.AttributesTag"/>.
/// </para>
/// </remarks>
public record GetCommandAttributesQuery(AnySharpObject SharpObject) : IQuery<CommandAttributeCache[]>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.Commands(SharpObject.Object().DBRef);
	public string[] CacheTags => [Definitions.CacheTags.InheritedAttributes];
}

/// <summary>
/// Cache entry for command attributes with pre-compiled regex patterns.
/// </summary>
public record CommandAttributeCache(
	SharpAttribute Attribute,
	Regex CompiledRegex,
	bool IsRegexFlag);
