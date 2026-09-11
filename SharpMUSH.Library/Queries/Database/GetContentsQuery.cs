using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetContentsQuery(DbRefOrContainer DBRef)
	: IStreamQuery<AnySharpContent>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.Contents(Container);
	// Both: the per-container tag is what a move invalidates; the broad one is still what a delete
	// reaches for, since severing an object's edges touches containers it cannot name.
	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectContents,
		Definitions.CacheKeys.ContentsTag(Container.Number)
	];

	private DBRef Container => DBRef switch
	{
		DBRef dbref => dbref,
		AnySharpContainer container => container.Object().DBRef
	};
}