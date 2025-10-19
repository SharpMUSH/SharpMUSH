using Mediator;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetContentsQuery(OneOf<DBRef, AnySharpContainer> DBRef) : IQuery<IAsyncEnumerable<AnySharpContent>?>/*, ICacheable*/
{
	public string CacheKey => $"object-contents:{DBRef.Match(x=> x, y=> y.Object().DBRef)}";
	public string[] CacheTags => [Definitions.CacheTags.ObjectContents];
}