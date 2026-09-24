using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateMailAliasCommand(SharpMailAlias Alias) : ICommand<Result<SharpMailAlias>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.MailAliasList];
}

/// <summary>Replaces the alias named <paramref name="Name"/>; a different <c>Alias.Name</c> renames it.</summary>
public record UpdateMailAliasCommand(string Name, SharpMailAlias Alias) : ICommand<Result<SharpMailAlias>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.MailAliasList];
}

public record DeleteMailAliasCommand(string Name) : ICommand<Found<None>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.MailAliasList];
}

public record DeleteAllMailAliasesCommand : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.MailAliasList];
}

/// <summary>PennMUSH <c>malias_cleanup</c> for a destroyed player.</summary>
public record ReleaseMailAliasesCommand(int Player, int NewOwner) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.MailAliasList];
}
