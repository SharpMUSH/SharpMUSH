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
	string Cause = "move")
	: ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys => [
		$"object-contents:{Target.Object().DBRef}",
		$"object-contents:{Destination.Object().DBRef}",
		$"object:{Target.Object().DBRef}",
		$"object:{Destination.Object().DBRef}"
	];

	public string[] CacheTags => [Definitions.CacheTags.ObjectContents];
}