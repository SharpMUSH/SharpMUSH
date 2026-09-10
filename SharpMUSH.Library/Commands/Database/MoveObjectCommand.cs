using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <param name="OldContainer">
/// The container <paramref name="Target"/> is leaving. Required, and required to be accurate: it is
/// the only source of the origin container's cache key and tag, and a move that named the wrong one
/// would leave a stale contents list behind.
/// </param>
public record MoveObjectCommand(
	AnySharpContent Target,
	AnySharpContainer Destination,
	DBRef OldContainer,
	DBRef? Enactor = null,
	bool IsSilent = false,
	string Cause = "move")
	: ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		Definitions.CacheKeys.Contents(OldContainer),
		Definitions.CacheKeys.Contents(Destination.Object().DBRef),
		Definitions.CacheKeys.Object(Target.Object().DBRef),
		Definitions.CacheKeys.Object(Destination.Object().DBRef)
	];

	/// <summary>
	/// Always invalidates the moved object's location (both the number-keyed and graph-id-keyed location
	/// caches, via per-object tags that clear every depth), and both containers' contents.
	/// </summary>
	/// <remarks>
	/// The contents keys are not sufficient on their own. A key removal drops what is cached at that
	/// instant, so a contents read that began before the move stores its pre-move list afterwards and
	/// the mover is missing from the destination until something else clears the key. Only a tag
	/// invalidation is resolved against when the reading factory started, so both containers need a
	/// tag as well. Per container, and never <see cref="Definitions.CacheTags.ObjectContents"/>, which
	/// would wipe every container's contents on every step.
	/// </remarks>
	public string[] CacheTags =>
	[
		Definitions.CacheKeys.LocationTag(Target.Object().DBRef.Number),
		Definitions.CacheKeys.LocationTag(Target.Object().Id!), // base Object().Id — matches GetCertainLocationQuery cache identity
		Definitions.CacheKeys.ContentsTag(Destination.Object().DBRef.Number),
		Definitions.CacheKeys.ContentsTag(OldContainer.Number)
	];
}
