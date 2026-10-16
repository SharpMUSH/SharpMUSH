using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>Account credentials are uncached; each decision is made in the provider write transaction.</summary>
public record SetAccountCharacterPasswordLoginCommand(string AccountId, bool Enabled) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}

public record RemoveAccountPasswordCommand(string AccountId, IReadOnlyCollection<string> EnabledRegistrations)
	: ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}

public record RemoveAccountPasskeyCommand(string AccountId, byte[] CredentialId, IReadOnlyCollection<string> EnabledRegistrations)
	: ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}
