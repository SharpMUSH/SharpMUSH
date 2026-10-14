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

	// The list is stored as refs and each read resolves them through the node cache, so a write to a
	// member is seen without a tag of its own; membership changes only by a move, a create or a delete,
	// which expire the tags above. A tag per member made each read check every one: a full room paid
	// about a microsecond per item before a name was compared.
	public bool TagsEmbeddedObjects => false;

	private DBRef Container => DBRef switch
	{
		DBRef dbref => dbref,
		AnySharpContainer container => container.Object().DBRef
	};
}