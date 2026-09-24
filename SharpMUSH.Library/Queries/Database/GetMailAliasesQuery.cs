using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>Every mail alias in creation order.</summary>
public record GetMailAliasesQuery : IStreamQuery<SharpMailAlias>, ICacheable
{
	public string CacheKey => "global:MailAliasList";

	public string[] CacheTags => [Definitions.CacheTags.MailAliasList];
}
