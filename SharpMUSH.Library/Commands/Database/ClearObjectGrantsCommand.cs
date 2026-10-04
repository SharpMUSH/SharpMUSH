using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Takes every role and override off <paramref name="Target"/>, without a permission check: PennMUSH's
/// privilege reset when an object changes hands or zones (<c>clear_flag_internal</c> on WIZARD and
/// ROYALTY, and the power bitmask zeroed), which the engine applies whoever asked.
/// </summary>
public record ClearObjectGrantsCommand(AnySharpObject Target) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef)];
	public string[] CacheTags => [];
}
