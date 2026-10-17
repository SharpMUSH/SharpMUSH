using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
namespace SharpMUSH.Library.Commands.Database;

/// <summary>The caller authorizes this security mutation with a current, explicit-account proof.</summary>
public record SetAccountEmailCommand(string AccountId, string? Email) : ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}
