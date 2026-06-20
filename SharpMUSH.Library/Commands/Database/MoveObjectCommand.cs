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
	/// caches, via per-object tags that clear every depth). Falls back to the broad
	/// <see cref="Definitions.CacheTags.ObjectContents"/> tag only when the caller has not supplied
	/// <see cref="OldContainer"/>; when it is provided the specific contents keys above are sufficient.
	/// </summary>
	public string[] CacheTags =>
	[
		Definitions.CacheKeys.LocationTag(Target.Object().DBRef.Number),
		Definitions.CacheKeys.LocationTag(Target.Object().Id!), // base Object().Id — matches GetCertainLocationQuery cache identity
		.. OldContainer is null ? (string[]) [Definitions.CacheTags.ObjectContents] : []
	];
}