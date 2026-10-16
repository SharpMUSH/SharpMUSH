using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>External credentials are never cached. These mutations therefore have no cache entries to expire.</summary>
public record ProvisionExternalAccountCommand(ExternalIdentityKey Key, string Username, string? Label)
	: ICommand<Result<SharpAccount>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}

public record AttachExternalIdentityCommand(string AccountId, ExternalIdentityKey Key, string? Label)
	: ICommand<Result<ExternalIdentity>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}

public record RecordExternalIdentityUseCommand(string AccountId, ExternalIdentityKey Key, DateTimeOffset UsedAt)
	: ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}

public record RemoveExternalIdentityCommand(string AccountId, ExternalIdentityKey Key, IReadOnlyCollection<string> EnabledRegistrations)
	: ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}
