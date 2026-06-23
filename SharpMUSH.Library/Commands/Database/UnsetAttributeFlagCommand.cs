using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record UnsetAttributeFlagCommand(DBRef DbRef, SharpAttribute Target, SharpAttributeFlag Flag) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		$"attribute:{DbRef}:{Target.LongName})",
		$"commands:{DbRef}",
		$"ancestor-commands:#{DbRef.Number}",
		$"ancestor-listens:#{DbRef.Number}"
	];
	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}
