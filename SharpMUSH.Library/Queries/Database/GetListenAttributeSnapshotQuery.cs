using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>Complete local attributes, including definitions that mask inherited listen patterns.</summary>
public record GetListenAttributeSnapshotQuery(DBRef Object) : IQuery<SharpAttribute[]>, ICacheable
{
	public string CacheKey => $"listen-attribute-snapshot:{Object}";
	public string[] CacheTags => [Definitions.CacheTags.InheritedAttributes];
}
