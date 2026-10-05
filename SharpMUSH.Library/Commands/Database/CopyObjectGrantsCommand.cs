using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Gives <paramref name="Target"/> every role and override <paramref name="Source"/> holds itself (not
/// through an account): <c>@clone/preserve</c> carrying WIZARD, ROYALTY and the powers across, which
/// PennMUSH does after its own wizard check (<c>create.c:640-652</c>).
/// </summary>
public record CopyObjectGrantsCommand(AnySharpObject Source, AnySharpObject Target) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef)];
	public string[] CacheTags => [];
}
