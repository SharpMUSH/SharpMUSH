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
			$"object-contents:{OldContainer}",
			$"object-contents:{Destination.Object().DBRef}",
			$"object:{Target.Object().DBRef}",
			$"object:{Destination.Object().DBRef}"
		]
		: [
			$"object:{Target.Object().DBRef}",
			$"object:{Destination.Object().DBRef}"
		];

	/// <summary>
	/// Falls back to the broad <see cref="Definitions.CacheTags.ObjectContents"/> tag only when
	/// the caller has not supplied <see cref="OldContainer"/>.  When <see cref="OldContainer"/> is
	/// provided the specific cache keys above are sufficient.
	/// </summary>
	public string[] CacheTags => OldContainer is null
		? [Definitions.CacheTags.ObjectContents]
		: [];
}