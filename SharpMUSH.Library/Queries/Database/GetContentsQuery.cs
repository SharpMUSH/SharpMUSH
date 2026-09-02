using Mediator;
using OneOf;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetContentsQuery(OneOf<DBRef, AnySharpContainer> DBRef)
	: IStreamQuery<AnySharpContent>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.Contents(DBRef.Match(x => x, y => y.Object().DBRef));
	// Both: the per-container tag is what a move invalidates; the broad one is still what a delete
	// reaches for, since severing an object's edges touches containers it cannot name.
	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectContents,
		Definitions.CacheKeys.ContentsTag(DBRef.Match(x => x, y => y.Object().DBRef).Number)
	];
}