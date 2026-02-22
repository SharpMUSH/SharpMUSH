using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record UnsetAttributeFlagCommand(DBRef DbRef, SharpAttribute Target, SharpAttributeFlag Flag) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		$"attribute:{DbRef}:{Target.LongName})",
		$"commands:{DbRef}"
	];
	public string[] CacheTags => [];
}
