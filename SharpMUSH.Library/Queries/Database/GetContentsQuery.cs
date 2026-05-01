using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetContentsQuery(DbRefOrContainer DBRef)
	: IStreamQuery<AnySharpContent>, ICacheable
{
	public string CacheKey => $"object-contents:{(DBRef.Value is DBRef d ? d : ((AnySharpContainer)DBRef.Value!).Object().DBRef)}";
	public string[] CacheTags => [Definitions.CacheTags.ObjectContents];
}
