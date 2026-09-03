using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record MoveObjectCommand(
	AnySharpContent Target,
	AnySharpContainer Destination,
	DBRef? Enactor = null,
	bool IsSilent = false,
	string Cause = "move",
	DBRef? OldContainer = null)
	: ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys => OldContainer is not null
		? [
			Definitions.CacheKeys.Contents(OldContainer.Value),
			Definitions.CacheKeys.Contents(Destination.Object().DBRef),
			Definitions.CacheKeys.Object(Target.Object().DBRef),
			Definitions.CacheKeys.Object(Destination.Object().DBRef)
		]
		: [
			Definitions.CacheKeys.Object(Target.Object().DBRef),
			Definitions.CacheKeys.Object(Destination.Object().DBRef)
		];

	/// <summary>
	/// Always invalidates the moved object's location (both the number-keyed and graph-id-keyed location
	/// caches, via per-object tags that clear every depth), and both containers' contents.
	/// </summary>
	/// <remarks>
	/// The contents keys above are not sufficient on their own, which is what this used to claim. A key
	/// removal drops what is cached at that instant, so a contents read that began before the move stores
	/// its pre-move list afterwards and the mover is missing from the destination until something else
	/// clears the key. Only a tag invalidation is resolved against when the reading factory started.
	/// Per container, because <see cref="Definitions.CacheTags.ObjectContents"/> would wipe every
	/// container's contents on every step.
	/// </remarks>
	public string[] CacheTags =>
	[
		Definitions.CacheKeys.LocationTag(Target.Object().DBRef.Number),
		Definitions.CacheKeys.LocationTag(Target.Object().Id!), // base Object().Id — matches GetCertainLocationQuery cache identity
		Definitions.CacheKeys.ContentsTag(Destination.Object().DBRef.Number),
		.. OldContainer is null
			? (string[]) [Definitions.CacheTags.ObjectContents]
			: [Definitions.CacheKeys.ContentsTag(OldContainer.Value.Number)]
	];
}